using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using BarberBooking.Web.Data;
using BarberBooking.Web.Models;
using Bladez.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BarberBooking.Web.Services;

public sealed class BookingService(BarberDbContext db, Actor actor, ShopClock clock, BookingAccess access, Audit audit, DataVault vault) : IBookingService
{
    public Task<List<Barber>> GetActiveBarbersAsync() => db.Barbers.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Id).ToListAsync();
    public Task<List<ServiceItem>> GetActiveServicesAsync() => db.Services.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Category).ThenBy(s => s.Price).ToListAsync();

    private IQueryable<Appointment> StaffQuery()
    {
        actor.RequireStaff();
        var query = db.Appointments.AsQueryable();
        return actor.IsBarber ? query.Where(a => a.BarberId == actor.BarberId) : query;
    }

    public async Task<List<TimeSlotDto>> GetAvailableSlotsAsync(int barberId, int serviceId, DateTime date)
    {
        var settings = await clock.SettingsAsync(); var now = clock.Now(settings);
        if (!settings.OnlineBookingEnabled) return [];
        ValidateDate(date, now, settings);
        var barber = await db.Barbers.AsNoTracking().SingleOrDefaultAsync(b => b.Id == barberId && b.IsActive);
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(s => s.Id == serviceId && s.IsActive);
        if (barber is null || service is null || !Works(barber, date)) return [];
        var target = date.Date;
        var occupied = await db.Appointments.Where(a => a.BarberId == barberId && a.AppointmentDate == target && a.Status != AppointmentStatus.Cancelled)
            .Select(a => new { a.StartTime, a.EndTime }).ToListAsync();
        var breaks = await db.TimeOff.Where(t => t.BarberId == barberId && t.Date == target).Select(t => new { t.StartTime, t.EndTime }).ToListAsync();
        var result = new List<TimeSlotDto>(); var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        for (var start = barber.WorkStartTime; start + duration <= barber.WorkEndTime; start += TimeSpan.FromMinutes(30))
        {
            var end = start + duration;
            var available = target.Add(start) >= now.AddMinutes(settings.LeadMinutes)
                && !occupied.Any(a => Overlaps(start, end, a.StartTime, a.EndTime))
                && !breaks.Any(t => Overlaps(start, end, t.StartTime, t.EndTime));
            result.Add(new TimeSlotDto { Time = start, IsAvailable = available, Reason = available ? null : "Недоступно" });
        }
        return result;
    }

    public async Task<Appointment> CreateAppointmentAsync(BookingRequestDto request, string? guestKey = null, bool forStaff = false)
    {
        if (forStaff) actor.RequireManager();
        var settings = await clock.SettingsAsync(); var now = clock.Now(settings);
        if (!forStaff && !settings.OnlineBookingEnabled) throw new InvalidOperationException("Онлайн-запись временно закрыта.");
        if (!forStaff && actor.Id is null && !settings.AllowGuestBooking) throw new UnauthorizedAccessException();
        ValidateDate(request.Date, now, settings);
        if (!TimeOnly.TryParseExact(request.TimeSlot, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ArgumentException("Укажите время в формате ЧЧ:ММ.");
        if (string.IsNullOrWhiteSpace(request.ClientName) || request.ClientName.Trim().Length is < 2 or > 100
            || request.Notes?.Length > 300 || request.ClientEmail?.Length > 100)
            throw new ArgumentException("Проверьте имя и контактные данные.");
        var phone = NormalizePhone(request.ClientPhone);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await LockBarber(request.BarberId);
        var barber = await db.Barbers.SingleAsync(b => b.Id == request.BarberId);
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.ServiceId && s.IsActive)
            ?? throw new ArgumentException("Услуга недоступна.");
        var start = parsed.ToTimeSpan(); var end = start + TimeSpan.FromMinutes(service.DurationMinutes);
        ValidateShift(barber, request.Date, start, end, now, settings);
        await EnsureFree(barber.Id, request.Date.Date, start, end);
        if (!forStaff && actor.Id is not null && await db.Appointments.CountAsync(a => a.CustomerUserId == actor.Id
            && a.AppointmentDate >= now.Date && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)) >= 8)
            throw new InvalidOperationException("Доступно не больше 8 предстоящих записей. Отмените ненужные или свяжитесь с администратором.");
        var customerId = forStaff ? null : actor.Id;
        if (customerId is null && string.IsNullOrWhiteSpace(guestKey)) throw new InvalidOperationException("Нужен секретный ключ гостевого талона.");
        var appointment = new Appointment
        {
            BookingCode = "BRB-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            BarberId = barber.Id, ServiceId = service.Id, ClientName = request.ClientName.Trim(), ClientPhone = phone,
            ClientEmail = string.IsNullOrWhiteSpace(request.ClientEmail) ? null : request.ClientEmail.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CustomerUserId = customerId, TicketHash = customerId is null ? BookingAccess.Hash(guestKey!) : null,
            PhoneFingerprint = vault.Fingerprint(phone, "Phone"), ServiceNameSnapshot = service.Name, DurationSnapshot = service.DurationMinutes,
            AppointmentDate = request.Date.Date, StartTime = start, EndTime = end, Price = service.Price,
            Status = AppointmentStatus.Pending, CreatedAt = DateTime.UtcNow
        };
        db.Appointments.Add(appointment);
        audit.Add("booking.created", appointment.BookingCode);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        appointment.Barber = barber; appointment.Service = service;
        return appointment;
    }

    public async Task<Appointment?> GetAppointmentByCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 20) return null;
        var clean = code.Trim().ToUpperInvariant();
        return await db.Appointments.Include(a => a.Barber).Include(a => a.Service).SingleOrDefaultAsync(a => a.BookingCode == clean);
    }

    public Task<List<Appointment>> GetMyAppointmentsAsync()
    {
        if (actor.Id is null) throw new UnauthorizedAccessException();
        return db.Appointments.AsNoTracking().Include(a => a.Barber).Include(a => a.Service).Where(a => a.CustomerUserId == actor.Id)
            .OrderByDescending(a => a.AppointmentDate).ThenByDescending(a => a.StartTime).Take(200).ToListAsync();
    }

    public async Task<ScheduleTimelineViewModel> GetScheduleTimelineAsync(DateTime date)
    {
        actor.RequireStaff();
        var target = date.Date;
        var barbers = await db.Barbers.AsNoTracking().Where(b => b.IsActive && (!actor.IsBarber || b.Id == actor.BarberId)).OrderBy(b => b.Id).ToListAsync();
        var appointments = await StaffQuery().AsNoTracking().Include(a => a.Barber).Include(a => a.Service)
            .Where(a => a.AppointmentDate == target && a.Status != AppointmentStatus.Cancelled).OrderBy(a => a.StartTime).Take(500).ToListAsync();
        return new ScheduleTimelineViewModel
        {
            SelectedDate = target, Barbers = barbers, Appointments = appointments.Select(ToDto).ToList(),
            DayStart = barbers.Count == 0 ? TimeSpan.FromHours(10) : barbers.Min(b => b.WorkStartTime),
            DayEnd = barbers.Count == 0 ? TimeSpan.FromHours(21) : barbers.Max(b => b.WorkEndTime)
        };
    }

    public async Task<AdminDashboardViewModel> GetAdminDashboardStatsAsync()
    {
        actor.RequireManager();
        var today = (await clock.NowAsync()).Date;
        var appointments = await StaffQuery().AsNoTracking().Include(a => a.Barber).Include(a => a.Service)
            .Where(a => a.AppointmentDate == today && a.Status != AppointmentStatus.Cancelled).OrderBy(a => a.StartTime).Take(500).ToListAsync();
        return new AdminDashboardViewModel
        {
            TodayAppointmentsCount = appointments.Count,
            TodayRevenue = appointments.Where(a => a.Status == AppointmentStatus.Completed).Sum(a => a.Price),
            PendingCount = await db.Appointments.CountAsync(a => a.Status == AppointmentStatus.Pending && a.AppointmentDate >= today),
            TotalClientsCount = await db.Appointments.Select(a => a.PhoneFingerprint).Distinct().CountAsync(),
            TodayAppointments = appointments.Select(ToDto).ToList(), ActiveBarbers = await GetActiveBarbersAsync(), PopularServices = await GetActiveServicesAsync()
        };
    }

    public async Task<List<CalendarAppointmentDto>> GetFilteredAppointmentsAsync(DateTime? fromDate, DateTime? toDate, int? barberId, AppointmentStatus? status, string? search)
    {
        var today = (await clock.NowAsync()).Date;
        var from = (fromDate ?? today.AddDays(-7)).Date; var to = (toDate ?? from.AddDays(30)).Date;
        if (to < from || (to - from).TotalDays > 31) throw new ArgumentException("Выберите период не длиннее 31 дня.");
        var query = StaffQuery().AsNoTracking().Include(a => a.Barber).Include(a => a.Service).Where(a => a.AppointmentDate >= from && a.AppointmentDate <= to);
        if (barberId is > 0) query = query.Where(a => a.BarberId == barberId);
        if (status.HasValue && Enum.IsDefined(status.Value)) query = query.Where(a => a.Status == status);
        // Encrypted contacts are searched only after role/date scoping, never with LIKE over ciphertext.
        var list = await query.OrderByDescending(a => a.AppointmentDate).ThenByDescending(a => a.StartTime).Take(1000).ToListAsync();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim()[..Math.Min(search.Trim().Length, 100)];
            list = list.Where(a => a.ClientName.Contains(term, StringComparison.OrdinalIgnoreCase) || a.ClientPhone.Contains(term, StringComparison.OrdinalIgnoreCase)
                || a.BookingCode.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return list.Take(200).Select(ToDto).ToList();
    }

    public async Task<bool> UpdateStatusAsync(int id, AppointmentStatus status, Guid version)
    {
        actor.RequireStaff();
        if (!Enum.IsDefined(status)) return false;
        var row = await StaffQuery().SingleOrDefaultAsync(a => a.Id == id);
        if (row is null) return false;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await LockBarber(row.BarberId); await db.Entry(row).ReloadAsync();
        if (row.Version != version || !CanTransition(row.Status, status)) return false;
        if (actor.IsBarber && status is not (AppointmentStatus.Completed or AppointmentStatus.NoShow)) return false;
        var now = await clock.NowAsync();
        if (status == AppointmentStatus.Completed && row.AppointmentDate.Add(row.StartTime) > now
            || status == AppointmentStatus.NoShow && row.AppointmentDate.Add(row.EndTime) > now) return false;
        row.Status = status; row.Version = Guid.NewGuid();
        audit.Add("booking.status." + status, row.BookingCode);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return true;
    }

    public async Task<bool> RescheduleAsync(int id, DateTime date, TimeSpan start, Guid version)
    {
        actor.RequireManager();
        var row = await db.Appointments.SingleOrDefaultAsync(a => a.Id == id);
        if (row is null) return false;
        var settings = await clock.SettingsAsync(); var now = clock.Now(settings); ValidateDate(date, now, settings);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await LockBarber(row.BarberId); await db.Entry(row).ReloadAsync();
        if (row.Version != version || row.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed)) return false;
        var barber = await db.Barbers.SingleAsync(b => b.Id == row.BarberId);
        var end = start + TimeSpan.FromMinutes(row.DurationSnapshot);
        ValidateShift(barber, date, start, end, now, settings); await EnsureFree(barber.Id, date.Date, start, end, row.Id);
        row.AppointmentDate = date.Date; row.StartTime = start; row.EndTime = end; row.Version = Guid.NewGuid();
        audit.Add("booking.rescheduled", row.BookingCode);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return true;
    }

    public async Task<bool> CancelByClientAsync(string code, string reason, Guid version)
    {
        var row = await GetAppointmentByCodeAsync(code);
        if (row is null || !access.CanRead(row)) return false;
        var settings = await clock.SettingsAsync(); var now = clock.Now(settings);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await LockBarber(row.BarberId); await db.Entry(row).ReloadAsync();
        if (row.Version != version || row.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed)
            || row.AppointmentDate.Add(row.StartTime) < now.AddMinutes(settings.CancellationLeadMinutes)) return false;
        row.Status = AppointmentStatus.Cancelled; row.CancellationReason = reason.Trim()[..Math.Min(reason.Trim().Length, 200)]; row.Version = Guid.NewGuid();
        audit.Add("booking.cancelled_by_customer", row.BookingCode);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return true;
    }

    private async Task LockBarber(int id)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT Id FROM Barbers WHERE Id = @id FOR UPDATE";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = id; command.Parameters.Add(parameter);
        if (await command.ExecuteScalarAsync() is null) throw new ArgumentException("Мастер недоступен.");
    }
    private async Task EnsureFree(int barberId, DateTime date, TimeSpan start, TimeSpan end, int ignoreId = 0)
    {
        if (await db.Appointments.AnyAsync(a => a.Id != ignoreId && a.BarberId == barberId && a.AppointmentDate == date
            && a.Status != AppointmentStatus.Cancelled && a.StartTime < end && a.EndTime > start)
            || await db.TimeOff.AnyAsync(t => t.BarberId == barberId && t.Date == date && t.StartTime < end && t.EndTime > start))
            throw new DoubleBookingConflictException("Время уже занято. Выберите другой свободный слот.");
    }
    private static bool Works(Barber barber, DateTime date) => barber.WorkingDays.Split(',').Contains(((int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture));
    private static void ValidateDate(DateTime date, DateTime now, ShopSettings settings)
    {
        if (date.Date < now.Date || date.Date > now.Date.AddDays(settings.BookingHorizonDays)) throw new ArgumentException("Дата за пределами доступного периода записи.");
    }
    private static void ValidateShift(Barber barber, DateTime date, TimeSpan start, TimeSpan end, DateTime now, ShopSettings settings)
    {
        if (!barber.IsActive || !Works(barber, date) || start < barber.WorkStartTime || end > barber.WorkEndTime
            || end <= start || (start - barber.WorkStartTime).Ticks % TimeSpan.FromMinutes(30).Ticks != 0
            || date.Date.Add(start) < now.AddMinutes(settings.LeadMinutes))
            throw new ArgumentException("Время недоступно: проверьте рабочий день, смену и шаг записи 30 минут.");
    }
    public static bool Overlaps(TimeSpan start, TimeSpan end, TimeSpan otherStart, TimeSpan otherEnd) => start < otherEnd && end > otherStart;
    public static bool CanTransition(AppointmentStatus from, AppointmentStatus to) => from switch
    {
        AppointmentStatus.Pending => to is AppointmentStatus.Confirmed or AppointmentStatus.Cancelled,
        AppointmentStatus.Confirmed => to is AppointmentStatus.Completed or AppointmentStatus.Cancelled or AppointmentStatus.NoShow,
        _ => false
    };
    public static string NormalizePhone(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 30) throw new ArgumentException("Укажите телефон Кыргызстана.");
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 10 && digits[0] == '0') digits = "996" + digits[1..];
        else if (digits.Length == 9) digits = "996" + digits;
        if (digits.Length != 12 || !digits.StartsWith("996", StringComparison.Ordinal)) throw new ArgumentException("Укажите телефон Кыргызстана: +996 и 9 цифр.");
        return "+" + digits;
    }
    private CalendarAppointmentDto ToDto(Appointment a) => new()
    {
        Id = a.Id, BookingCode = a.BookingCode, BarberId = a.BarberId, BarberName = a.Barber.FullName, BarberAvatar = a.Barber.AvatarUrl,
        BadgeColor = a.Barber.BadgeColor, ServiceId = a.ServiceId, ServiceName = a.ServiceNameSnapshot, DurationMinutes = a.DurationSnapshot,
        ClientName = a.ClientName, ClientPhone = actor.IsBarber ? "••• " + a.ClientPhone[^4..] : a.ClientPhone, Notes = a.Notes,
        Date = a.AppointmentDate, StartTime = a.StartTime, EndTime = a.EndTime, Price = a.Price, Status = a.Status, Version = a.Version
    };
}
