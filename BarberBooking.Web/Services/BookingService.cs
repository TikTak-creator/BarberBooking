using System.Data;
using BarberBooking.Web.Data;
using BarberBooking.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberBooking.Web.Services;

public class BookingService : IBookingService
{
    private readonly BarberDbContext _context;
    private readonly ILogger<BookingService> _logger;

    public BookingService(BarberDbContext context, ILogger<BookingService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<Barber>> GetActiveBarbersAsync()
    {
        return await _context.Barbers
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .ToListAsync();
    }

    public async Task<Barber?> GetBarberByIdAsync(int id)
    {
        return await _context.Barbers.FindAsync(id);
    }

    public async Task<List<ServiceItem>> GetActiveServicesAsync()
    {
        return await _context.Services
            .Where(s => s.IsActive)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Price)
            .ToListAsync();
    }

    public async Task<ServiceItem?> GetServiceByIdAsync(int id)
    {
        return await _context.Services.FindAsync(id);
    }

    /// <summary>
    /// Расчет доступных слотов времени с защитой от пересечения и учетом рабочих часов мастера.
    /// </summary>
    public async Task<List<TimeSlotDto>> GetAvailableSlotsAsync(int barberId, int serviceId, DateTime date)
    {
        var barber = await _context.Barbers.FindAsync(barberId);
        if (barber == null || !barber.IsActive)
            return new List<TimeSlotDto>();

        var service = await _context.Services.FindAsync(serviceId);
        if (service == null)
            return new List<TimeSlotDto>();

        var dayOfWeek = (int)date.DayOfWeek;
        var workingDays = (barber.WorkingDays ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var d) ? d : -1)
            .ToHashSet();

        // Если мастер в этот день не работает
        if (!workingDays.Contains(dayOfWeek))
        {
            return new List<TimeSlotDto>
            {
                new() { Time = TimeSpan.Zero, IsAvailable = false, Reason = "У мастера выходной день" }
            };
        }

        // Извлекаем все существующие активные записи на эту дату
        var targetDate = date.Date;
        var existingAppointments = await _context.Appointments
            .Where(a => a.BarberId == barberId
                     && a.AppointmentDate == targetDate
                     && a.Status != AppointmentStatus.Cancelled)
            .Select(a => new { a.StartTime, a.EndTime })
            .ToListAsync();

        var slots = new List<TimeSlotDto>();
        var step = TimeSpan.FromMinutes(30);
        var serviceDuration = TimeSpan.FromMinutes(service.DurationMinutes);
        var now = DateTime.Now;

        for (var current = barber.WorkStartTime; current + serviceDuration <= barber.WorkEndTime; current += step)
        {
            var slotEnd = current + serviceDuration;
            var isAvailable = true;
            string? reason = null;

            // 1. Проверка на прошедшее время (для сегодняшнего дня)
            if (date.Date == now.Date)
            {
                var slotDateTime = date.Date.Add(current);
                if (slotDateTime <= now.AddMinutes(15)) // за 15 минут до слота запись закрывается
                {
                    isAvailable = false;
                    reason = "Время уже прошло";
                }
            }

            // 2. Проверка пересечения с существующими записями:
            // Два интервала [current, slotEnd) и [existing.StartTime, existing.EndTime) пересекаются, если:
            // current < existing.EndTime && slotEnd > existing.StartTime
            if (isAvailable)
            {
                var hasConflict = existingAppointments.Any(e => current < e.EndTime && slotEnd > e.StartTime);
                if (hasConflict)
                {
                    isAvailable = false;
                    reason = "Время занято";
                }
            }

            slots.Add(new TimeSlotDto
            {
                Time = current,
                IsAvailable = isAvailable,
                Reason = reason
            });
        }

        return slots;
    }

    /// <summary>
    /// Создание бронирования с гарантированной защитой от Double Booking (в транзакции с проверкой).
    /// </summary>
    public async Task<Appointment> CreateAppointmentAsync(BookingRequestDto request)
    {
        var barber = await _context.Barbers.FindAsync(request.BarberId)
            ?? throw new ArgumentException("Выбранный мастер не найден.");

        var service = await _context.Services.FindAsync(request.ServiceId)
            ?? throw new ArgumentException("Выбранная услуга не найдена.");

        if (!TimeSpan.TryParse(request.TimeSlot, out var startTime))
            throw new ArgumentException("Некорректный формат времени.");

        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var endTime = startTime + duration;
        var appointmentDate = request.Date.Date;

        // Валидация рабочих часов
        if (startTime < barber.WorkStartTime || endTime > barber.WorkEndTime)
            throw new InvalidOperationException($"Запись должна быть в пределах рабочих часов мастера ({barber.WorkStartTime:hh\\:mm} – {barber.WorkEndTime:hh\\:mm}).");

        // Транзакция для защиты от race condition (двойного бронирования)
        using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            // Проверка конфликтов в базе
            var hasConflict = await _context.Appointments
                .AnyAsync(a => a.BarberId == request.BarberId
                            && a.AppointmentDate == appointmentDate
                            && a.Status != AppointmentStatus.Cancelled
                            && a.StartTime < endTime
                            && a.EndTime > startTime);

            if (hasConflict)
            {
                throw new DoubleBookingConflictException("Выбранное время у этого мастера уже занято другим клиентом. Пожалуйста, выберите другое свободное время.");
            }

            // Генерация уникального кода брони
            var bookingCode = GenerateBookingCode();
            while (await _context.Appointments.AnyAsync(a => a.BookingCode == bookingCode))
            {
                bookingCode = GenerateBookingCode();
            }

            var appointment = new Appointment
            {
                BookingCode = bookingCode,
                BarberId = request.BarberId,
                ServiceId = request.ServiceId,
                ClientName = request.ClientName.Trim(),
                ClientPhone = FormatPhone(request.ClientPhone),
                ClientEmail = string.IsNullOrWhiteSpace(request.ClientEmail) ? null : request.ClientEmail.Trim(),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                AppointmentDate = appointmentDate,
                StartTime = startTime,
                EndTime = endTime,
                Price = service.Price,
                Status = AppointmentStatus.Confirmed, // По умолчанию подтверждено
                CreatedAt = DateTime.UtcNow
            };

            await _context.Appointments.AddAsync(appointment);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Успешно создана запись {Code} для клиента {Client} к мастеру {Barber} на {Date} {Time}",
                appointment.BookingCode, appointment.ClientName, barber.FullName, appointment.AppointmentDate, appointment.StartTime);

            // Загружаем навигационные свойства
            appointment.Barber = barber;
            appointment.Service = service;

            return appointment;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning("Ошибка при создании записи: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<Appointment?> GetAppointmentByCodeAsync(string bookingCode)
    {
        var cleanCode = bookingCode.Trim().ToUpperInvariant();
        return await _context.Appointments
            .Include(a => a.Barber)
            .Include(a => a.Service)
            .FirstOrDefaultAsync(a => a.BookingCode == cleanCode);
    }

    public async Task<List<Appointment>> GetAppointmentsByPhoneAsync(string phone)
    {
        var cleanPhone = FormatPhone(phone);
        return await _context.Appointments
            .Include(a => a.Barber)
            .Include(a => a.Service)
            .Where(a => a.ClientPhone.Contains(cleanPhone) || cleanPhone.Contains(a.ClientPhone))
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .ToListAsync();
    }

    /// <summary>
    /// Данные для отображения интерактивного таймлайн-календаря (как на референсном скриншоте).
    /// </summary>
    public async Task<ScheduleTimelineViewModel> GetScheduleTimelineAsync(DateTime date)
    {
        var targetDate = date.Date;
        var barbers = await _context.Barbers
            .Where(b => b.IsActive)
            .OrderBy(b => b.Id)
            .ToListAsync();

        var appointments = await _context.Appointments
            .Include(a => a.Barber)
            .Include(a => a.Service)
            .Where(a => a.AppointmentDate == targetDate && a.Status != AppointmentStatus.Cancelled)
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        var dtos = appointments.Select(a => new CalendarAppointmentDto
        {
            Id = a.Id,
            BookingCode = a.BookingCode,
            BarberId = a.BarberId,
            BarberName = a.Barber.FullName,
            BarberAvatar = a.Barber.AvatarUrl,
            BadgeColor = a.Barber.BadgeColor,
            ServiceId = a.ServiceId,
            ServiceName = a.Service.Name,
            DurationMinutes = a.Service.DurationMinutes,
            ClientName = a.ClientName,
            ClientPhone = a.ClientPhone,
            Notes = a.Notes,
            Date = a.AppointmentDate,
            StartTime = a.StartTime,
            EndTime = a.EndTime,
            Price = a.Price,
            Status = a.Status
        }).ToList();

        return new ScheduleTimelineViewModel
        {
            SelectedDate = targetDate,
            Barbers = barbers,
            Appointments = dtos,
            DayStart = new TimeSpan(10, 0, 0),
            DayEnd = new TimeSpan(21, 0, 0)
        };
    }

    public async Task<AdminDashboardViewModel> GetAdminDashboardStatsAsync()
    {
        var today = DateTime.Today;

        var todayAppointments = await _context.Appointments
            .Include(a => a.Barber)
            .Include(a => a.Service)
            .Where(a => a.AppointmentDate == today && a.Status != AppointmentStatus.Cancelled)
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        var todayRevenue = todayAppointments
            .Where(a => a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.Confirmed)
            .Sum(a => a.Price);

        var pendingCount = await _context.Appointments
            .CountAsync(a => a.Status == AppointmentStatus.Pending && a.AppointmentDate >= today);

        var totalClients = await _context.Appointments
            .Select(a => a.ClientPhone)
            .Distinct()
            .CountAsync();

        var activeBarbers = await _context.Barbers
            .Where(b => b.IsActive)
            .ToListAsync();

        var popularServices = await _context.Services
            .Where(s => s.IsActive)
            .Take(5)
            .ToListAsync();

        var dtos = todayAppointments.Select(a => new CalendarAppointmentDto
        {
            Id = a.Id,
            BookingCode = a.BookingCode,
            BarberId = a.BarberId,
            BarberName = a.Barber.FullName,
            BarberAvatar = a.Barber.AvatarUrl,
            BadgeColor = a.Barber.BadgeColor,
            ServiceId = a.ServiceId,
            ServiceName = a.Service.Name,
            DurationMinutes = a.Service.DurationMinutes,
            ClientName = a.ClientName,
            ClientPhone = a.ClientPhone,
            Notes = a.Notes,
            Date = a.AppointmentDate,
            StartTime = a.StartTime,
            EndTime = a.EndTime,
            Price = a.Price,
            Status = a.Status
        }).ToList();

        return new AdminDashboardViewModel
        {
            TodayAppointmentsCount = todayAppointments.Count,
            TodayRevenue = todayRevenue,
            PendingCount = pendingCount,
            TotalClientsCount = totalClients,
            TodayAppointments = dtos,
            ActiveBarbers = activeBarbers,
            PopularServices = popularServices
        };
    }

    public async Task<List<CalendarAppointmentDto>> GetFilteredAppointmentsAsync(
        DateTime? fromDate, DateTime? toDate, int? barberId, AppointmentStatus? status, string? search)
    {
        var query = _context.Appointments
            .Include(a => a.Barber)
            .Include(a => a.Service)
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(a => a.AppointmentDate >= fromDate.Value.Date);

        if (toDate.HasValue)
            query = query.Where(a => a.AppointmentDate <= toDate.Value.Date);

        if (barberId.HasValue && barberId.Value > 0)
            query = query.Where(a => a.BarberId == barberId.Value);

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(a => a.ClientName.ToLower().Contains(s)
                                  || a.ClientPhone.Contains(s)
                                  || a.BookingCode.ToLower().Contains(s));
        }

        var list = await query
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .ToListAsync();

        return list.Select(a => new CalendarAppointmentDto
        {
            Id = a.Id,
            BookingCode = a.BookingCode,
            BarberId = a.BarberId,
            BarberName = a.Barber.FullName,
            BarberAvatar = a.Barber.AvatarUrl,
            BadgeColor = a.Barber.BadgeColor,
            ServiceId = a.ServiceId,
            ServiceName = a.Service.Name,
            DurationMinutes = a.Service.DurationMinutes,
            ClientName = a.ClientName,
            ClientPhone = a.ClientPhone,
            Notes = a.Notes,
            Date = a.AppointmentDate,
            StartTime = a.StartTime,
            EndTime = a.EndTime,
            Price = a.Price,
            Status = a.Status
        }).ToList();
    }

    public async Task<bool> UpdateStatusAsync(int id, AppointmentStatus status, string? reason = null)
    {
        var app = await _context.Appointments.FindAsync(id);
        if (app == null) return false;

        app.Status = status;
        if (!string.IsNullOrWhiteSpace(reason))
            app.CancellationReason = reason.Trim();

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RescheduleAsync(int id, DateTime newDate, TimeSpan newStartTime)
    {
        var app = await _context.Appointments
            .Include(a => a.Service)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (app == null) return false;

        var duration = TimeSpan.FromMinutes(app.Service.DurationMinutes);
        var newEndTime = newStartTime + duration;

        using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var conflict = await _context.Appointments
            .AnyAsync(a => a.Id != id
                        && a.BarberId == app.BarberId
                        && a.AppointmentDate == newDate.Date
                        && a.Status != AppointmentStatus.Cancelled
                        && a.StartTime < newEndTime
                        && a.EndTime > newStartTime);

        if (conflict)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException("Новое выбранное время занято другой записью.");
        }

        app.AppointmentDate = newDate.Date;
        app.StartTime = newStartTime;
        app.EndTime = newEndTime;
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return true;
    }

    public async Task<bool> CancelByClientAsync(string bookingCode, string reason)
    {
        var app = await GetAppointmentByCodeAsync(bookingCode);
        if (app == null || app.Status == AppointmentStatus.Cancelled)
            return false;

        app.Status = AppointmentStatus.Cancelled;
        app.CancellationReason = $"Отменено клиентом: {reason}";
        await _context.SaveChangesAsync();
        return true;
    }

    private static string GenerateBookingCode()
    {
        var random = Random.Shared.Next(1000, 9999);
        return $"BRB-{random}";
    }

    private static string FormatPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("996"))
            return $"+996 {digits.Substring(3, 3)} {digits.Substring(6, 3)}-{digits.Substring(9, 3)}";
        if (digits.Length == 10 && digits.StartsWith("0"))
            return $"+996 {digits.Substring(1, 3)} {digits.Substring(4, 3)}-{digits.Substring(7, 3)}";
        if (digits.Length == 9)
            return $"+996 {digits.Substring(0, 3)} {digits.Substring(3, 3)}-{digits.Substring(6, 3)}";
        return phone.Trim();
    }
}
