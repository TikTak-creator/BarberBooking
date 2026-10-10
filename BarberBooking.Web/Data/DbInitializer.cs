using BarberBooking.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberBooking.Web.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(BarberDbContext context)
    {
        // Создаем БД и таблицы если они не созданы
        await context.Database.EnsureCreatedAsync();

        if (await context.Barbers.AnyAsync())
        {
            return; // БД уже инициализирована
        }

        // 1. Добавляем мастеров
        var barbers = new List<Barber>
        {
            new()
            {
                FullName = "Азамат Исаев",
                Title = "Top Barber & Founder",
                Bio = "Победитель Central Asia Barber Battle. Специалист по премиальным стрижкам и фейдам любой сложности.",
                AvatarUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?auto=format&fit=crop&w=250&q=80",
                BadgeColor = "#F59E0B", // Теплый янтарный / желтый
                Rating = 5.0,
                ExperienceYears = 8,
                IsActive = true,
                WorkingDays = "1,2,3,4,5,6", // Пн-Сб
                WorkStartTime = new TimeSpan(10, 0, 0),
                WorkEndTime = new TimeSpan(21, 0, 0)
            },
            new()
            {
                FullName = "Эмиль Касымов",
                Title = "Senior Barber & Fade Master",
                Bio = "Мастер идеальных переходов Skin Fade, четких контуров и текстурных стрижек в стиле Crop.",
                AvatarUrl = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?auto=format&fit=crop&w=250&q=80",
                BadgeColor = "#06B6D4", // Бирюзовый / Cyan
                Rating = 4.9,
                ExperienceYears = 6,
                IsActive = true,
                WorkingDays = "1,2,3,4,5,0", // Пн-Пт, Вс
                WorkStartTime = new TimeSpan(10, 0, 0),
                WorkEndTime = new TimeSpan(21, 0, 0)
            },
            new()
            {
                FullName = "Руслан Султанов",
                Title = "Beard & Blade Specialist",
                Bio = "Гуру моделирования бород, традиционного королевского бритья с распариванием горячими полотенцами.",
                AvatarUrl = "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?auto=format&fit=crop&w=250&q=80",
                BadgeColor = "#8B5CF6", // Фиолетовый / Purple
                Rating = 4.9,
                ExperienceYears = 5,
                IsActive = true,
                WorkingDays = "2,3,4,5,6,0", // Вт-Вс
                WorkStartTime = new TimeSpan(10, 0, 0),
                WorkEndTime = new TimeSpan(21, 0, 0)
            },
            new()
            {
                FullName = "Данияр Беков",
                Title = "Pro Barber & Hair Stylist",
                Bio = "Классические британские стрижки, укладки и детские образы. Деликатный подход к каждому клиенту.",
                AvatarUrl = "https://images.unsplash.com/photo-1492562080023-ab3db95bfbce?auto=format&fit=crop&w=250&q=80",
                BadgeColor = "#10B981", // Изумрудно-зеленый
                Rating = 4.8,
                ExperienceYears = 4,
                IsActive = true,
                WorkingDays = "1,3,4,5,6,0", // Пн, Ср-Вс
                WorkStartTime = new TimeSpan(10, 0, 0),
                WorkEndTime = new TimeSpan(21, 0, 0)
            }
        };

        await context.Barbers.AddRangeAsync(barbers);
        await context.SaveChangesAsync();

        // 2. Добавляем услуги
        var services = new List<ServiceItem>
        {
            new()
            {
                Name = "Мужская стрижка",
                Description = "Мытье головы до и после, подбор формы, стрижка ножницами и машинкой, стайлинг премиум-косметикой.",
                Price = 900,
                DurationMinutes = 45,
                Category = "Стрижки",
                Icon = "bi-scissors",
                IsActive = true
            },
            new()
            {
                Name = "Моделирование бороды и усов",
                Description = "Придание четкой геометрии бороды, бритье контуров шаветкой, уход маслом и бальзамом.",
                Price = 500,
                DurationMinutes = 30,
                Category = "Борода",
                Icon = "bi-person-badge",
                IsActive = true
            },
            new()
            {
                Name = "Комплекс «Стрижка + Борода»",
                Description = "Полный образ: классическая или модельная стрижка + оформление бороды и усов.",
                Price = 1300,
                DurationMinutes = 60,
                Category = "Комплексы",
                Icon = "bi-stars",
                IsActive = true
            },
            new()
            {
                Name = "Королевское бритьё",
                Description = "Традиционное бритье опасной бритвой с распариванием эфирными маслами и компрессом.",
                Price = 800,
                DurationMinutes = 45,
                Category = "Бритьё",
                Icon = "bi-gem",
                IsActive = true
            },
            new()
            {
                Name = "Детская стрижка (до 12 лет)",
                Description = "Стильная и аккуратная стрижка для юных джентльменов в комфортной атмосфере.",
                Price = 700,
                DurationMinutes = 40,
                Category = "Стрижки",
                Icon = "bi-emoji-smile",
                IsActive = true
            },
            new()
            {
                Name = "Камуфляж седины и укладка",
                Description = "Мягкое тонирование седины без эффекта окрашенных волос, мытье и укладка.",
                Price = 600,
                DurationMinutes = 30,
                Category = "Уход",
                Icon = "bi-palette",
                IsActive = true
            }
        };

        await context.Services.AddRangeAsync(services);
        await context.SaveChangesAsync();

        // 3. Добавляем демо-записи на сегодня и соседние дни для наполнения календаря
        var today = DateTime.Today;
        var rnd = new Random(42);

        var demoAppointments = new List<Appointment>
        {
            // На сегодня
            new()
            {
                BookingCode = "BRB-1011",
                BarberId = barbers[0].Id, // Азамат
                ServiceId = services[2].Id, // Стрижка + Борода (60 мин)
                ClientName = "Алексей Смирнов",
                ClientPhone = "+996 555 123-456",
                ClientEmail = "alex.smirnov@gmail.com",
                Notes = "Классический пробор, бороду подровнять покороче",
                AppointmentDate = today,
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(11, 0, 0),
                Price = 1300,
                Status = AppointmentStatus.Confirmed,
                CreatedAt = today.AddDays(-2)
            },
            new()
            {
                BookingCode = "BRB-1012",
                BarberId = barbers[0].Id, // Азамат
                ServiceId = services[0].Id, // Стрижка (45 мин)
                ClientName = "Нурлан Омаров",
                ClientPhone = "+996 700 987-654",
                Notes = "Fade по бокам",
                AppointmentDate = today,
                StartTime = new TimeSpan(12, 0, 0),
                EndTime = new TimeSpan(12, 45, 0),
                Price = 900,
                Status = AppointmentStatus.Completed,
                CreatedAt = today.AddDays(-1)
            },
            new()
            {
                BookingCode = "BRB-1013",
                BarberId = barbers[0].Id, // Азамат
                ServiceId = services[3].Id, // Королевское бритьё
                ClientName = "Михаил Чен",
                ClientPhone = "+996 505 444-222",
                AppointmentDate = today,
                StartTime = new TimeSpan(15, 0, 0),
                EndTime = new TimeSpan(15, 45, 0),
                Price = 800,
                Status = AppointmentStatus.Confirmed,
                CreatedAt = today.AddHours(-3)
            },
            new()
            {
                BookingCode = "BRB-1021",
                BarberId = barbers[1].Id, // Эмиль
                ServiceId = services[0].Id, // Стрижка
                ClientName = "Бахтияр Кадыров",
                ClientPhone = "+996 772 333-111",
                Notes = "Низкий скин фейд",
                AppointmentDate = today,
                StartTime = new TimeSpan(11, 0, 0),
                EndTime = new TimeSpan(11, 45, 0),
                Price = 900,
                Status = AppointmentStatus.Confirmed,
                CreatedAt = today.AddDays(-1)
            },
            new()
            {
                BookingCode = "BRB-1022",
                BarberId = barbers[1].Id, // Эмиль
                ServiceId = services[2].Id, // Стрижка + Борода
                ClientName = "Дмитрий Волков",
                ClientPhone = "+996 550 888-999",
                AppointmentDate = today,
                StartTime = new TimeSpan(14, 0, 0),
                EndTime = new TimeSpan(15, 0, 0),
                Price = 1300,
                Status = AppointmentStatus.Confirmed,
                CreatedAt = today.AddDays(-2)
            },
            new()
            {
                BookingCode = "BRB-1031",
                BarberId = barbers[2].Id, // Руслан
                ServiceId = services[1].Id, // Моделирование бороды (30 мин)
                ClientName = "Эркин Токтогулов",
                ClientPhone = "+996 701 555-777",
                AppointmentDate = today,
                StartTime = new TimeSpan(10, 30, 0),
                EndTime = new TimeSpan(11, 0, 0),
                Price = 500,
                Status = AppointmentStatus.Completed,
                CreatedAt = today.AddDays(-1)
            },
            new()
            {
                BookingCode = "BRB-1032",
                BarberId = barbers[2].Id, // Руслан
                ServiceId = services[3].Id, // Королевское бритье
                ClientName = "Илья Ковалёв",
                ClientPhone = "+996 551 777-333",
                Notes = "Чувствительная кожа",
                AppointmentDate = today,
                StartTime = new TimeSpan(13, 0, 0),
                EndTime = new TimeSpan(13, 45, 0),
                Price = 800,
                Status = AppointmentStatus.Confirmed,
                CreatedAt = today.AddHours(-5)
            },
            new()
            {
                BookingCode = "BRB-1041",
                BarberId = barbers[3].Id, // Данияр
                ServiceId = services[4].Id, // Детская стрижка
                ClientName = "Марат (сын Алан, 7 лет)",
                ClientPhone = "+996 777 222-888",
                AppointmentDate = today,
                StartTime = new TimeSpan(11, 30, 0),
                EndTime = new TimeSpan(12, 10, 0),
                Price = 700,
                Status = AppointmentStatus.Confirmed,
                CreatedAt = today.AddDays(-3)
            },
            new()
            {
                BookingCode = "BRB-1042",
                BarberId = barbers[3].Id, // Данияр
                ServiceId = services[0].Id, // Мужская стрижка
                ClientName = "Канат Жумабеков",
                ClientPhone = "+996 558 909-102",
                AppointmentDate = today,
                StartTime = new TimeSpan(16, 0, 0),
                EndTime = new TimeSpan(16, 45, 0),
                Price = 900,
                Status = AppointmentStatus.Pending,
                CreatedAt = today.AddHours(-1)
            },
            // Запись на завтра
            new()
            {
                BookingCode = "BRB-1051",
                BarberId = barbers[0].Id,
                ServiceId = services[2].Id,
                ClientName = "Султан Бакиров",
                ClientPhone = "+996 708 111-222",
                AppointmentDate = today.AddDays(1),
                StartTime = new TimeSpan(11, 0, 0),
                EndTime = new TimeSpan(12, 0, 0),
                Price = 1300,
                Status = AppointmentStatus.Confirmed,
                CreatedAt = today
            },
            new()
            {
                BookingCode = "BRB-1052",
                BarberId = barbers[1].Id,
                ServiceId = services[0].Id,
                ClientName = "Артем Медведев",
                ClientPhone = "+996 555 333-999",
                AppointmentDate = today.AddDays(1),
                StartTime = new TimeSpan(13, 30, 0),
                EndTime = new TimeSpan(14, 15, 0),
                Price = 900,
                Status = AppointmentStatus.Confirmed,
                CreatedAt = today
            }
        };

        await context.Appointments.AddRangeAsync(demoAppointments);
        await context.SaveChangesAsync();
    }
}
