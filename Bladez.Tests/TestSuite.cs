using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BarberBooking.Web.Data;
using BarberBooking.Web.Models;
using BarberBooking.Web.Services;
using Bladez.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;

namespace Bladez.Tests;

internal static class TestSuite
{
    private static readonly List<string> passed = [];
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException("FAIL: " + name); passed.Add(name); Console.WriteLine("PASS: " + name); }
    private static HttpClient Client(WebApplicationFactory<global::Program> app) => app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
    private static async Task<string> Csrf(HttpClient client, string page)
    {
        using var response = await client.GetAsync(page); var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "<meta name=\"csrf-token\" content=\"([^\"]+)\"");
        if (!match.Success) throw new Exception("Missing CSRF token on " + page + " (" + response.StatusCode + ")");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static async Task<HttpResponseMessage> Form(HttpClient client, string page, string url, Dictionary<string,string> fields)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,url) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("X-CSRF-TOKEN",await Csrf(client,page)); return await client.SendAsync(request);
    }
    private static async Task<HttpResponseMessage> Json(HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN",await Csrf(client,"/Booking")); return await client.SendAsync(request);
    }
    private static async Task<HttpResponseMessage> Login(HttpClient client,string name,string password) => await Form(client,"/Account/Login","/Account/Login",new(){["UserName"]=name,["Password"]=password});
    private static async Task Register(HttpClient client,string name,string password) => Check((await Form(client,"/Account/Register","/Account/Register",new()
    { ["UserName"]=name,["DisplayName"]="Test " + name,["Password"]=password,["ConfirmPassword"]=password,["AcceptPrivacy"]="true",["Role"]="Owner" })).StatusCode==HttpStatusCode.Redirect,"Client registration: "+name);

    public static async Task RunAsync(WebApplicationFactory<global::Program> app,HostSettings settings,DataVault vault,string ownerPassword,string repo,string output)
    {
        try
        {
            var value = "Контакт +996555123456"; var cipher = vault.Encrypt(value,"test");
            Check(cipher != vault.Encrypt(value,"test") && vault.Decrypt(cipher,"test")==value,"AES-GCM random nonce and round trip");
            var rejected=false; try{vault.Decrypt(cipher,"wrong-purpose");}catch(CryptographicException){rejected=true;} Check(rejected,"AES-GCM purpose binding");
            rejected=false; try{vault.Decrypt(cipher[..^2]+"AA","test");}catch(Exception e) when(e is CryptographicException or FormatException){rejected=true;} Check(rejected,"AES-GCM tampering rejected");
            Check(new DataVault(output).Decrypt(cipher,"test")==value,"Data key survives process/profile reload");
            Check(BookingService.Overlaps(TimeSpan.FromHours(10),TimeSpan.FromHours(11),TimeSpan.FromHours(10.5),TimeSpan.FromHours(12))
                && !BookingService.Overlaps(TimeSpan.FromHours(10),TimeSpan.FromHours(11),TimeSpan.FromHours(11),TimeSpan.FromHours(12)),"Half-open intervals and adjacent appointments");
            Check(!BookingService.CanTransition(AppointmentStatus.Cancelled,AppointmentStatus.Confirmed)
                && !BookingService.CanTransition(AppointmentStatus.Completed,AppointmentStatus.Cancelled),"Terminal statuses cannot be revived");
            using var sql = new MySqlConnection(vault.ReadSecrets().ConnectionString); await sql.OpenAsync();
            await using(var command=sql.CreateCommand())
            { command.CommandText="CREATE TABLE forbidden_ddl (id INT)"; rejected=false; try{await command.ExecuteNonQueryAsync();}catch(MySqlException e) when(e.Number==1142){rejected=true;} Check(rejected,"Runtime database account cannot change schema"); }
            using var anonymous=Client(app); using var owner=Client(app); using var first=Client(app); using var second=Client(app);
            var response=await anonymous.GetAsync("/admin/timeline"); Check(response.StatusCode==HttpStatusCode.Redirect && response.Headers.Location!.ToString().Contains("Account/Login"),"Anonymous cannot read admin schedule");
            response=await anonymous.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"UserName","testowner"},{"Password",ownerPassword}})); Check(response.StatusCode==HttpStatusCode.BadRequest,"Login CSRF rejected");
            response=await Login(owner,"testowner",ownerPassword); Check(response.StatusCode==HttpStatusCode.Redirect,"Owner password login");
            Check(response.Headers.GetValues("Set-Cookie").Any(c=>c.Contains("Bladez.Session=")&&c.Contains("httponly",StringComparison.OrdinalIgnoreCase)&&c.Contains("secure",StringComparison.OrdinalIgnoreCase)),"Authentication cookie is HttpOnly and Secure over HTTPS");
            var password="TestOnly_A1_"+Guid.NewGuid().ToString("N");
            await Register(first,"customer_one",password); await Register(second,"customer_two",password);
            using(var scope=app.Services.CreateScope())
            {
                var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user=await users.FindByNameAsync("customer_one");
                Check(user is not null && await users.IsInRoleAsync(user,Roles.Customer) && !await users.IsInRoleAsync(user,Roles.Owner),"Registration cannot assign an Owner role");
                Check(user!.PasswordHash is not null && user.PasswordHash!=password && await users.CheckPasswordAsync(user,password),"Identity password hash, not reversible password storage");
            }
            response=await first.GetAsync("/admin/settings");Check(response.StatusCode==HttpStatusCode.Redirect && response.Headers.Location!.ToString().Contains("Denied"),"Customer cannot reach owner settings");
            var tomorrow=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Asia/Bishkek")).Date.AddDays(1);
            while(tomorrow.DayOfWeek==DayOfWeek.Sunday)tomorrow=tomorrow.AddDays(1);
            object Body(string name,string time,int barber=1,DateTime? day=null)=>new {barberId=barber,serviceId=1,date=(day??tomorrow).ToString("yyyy-MM-dd"),timeSlot=time,clientName=name,clientPhone="+996555123456",notes="<img src=x onerror=alert(1)>",price=1,customerUserId="someone-else"};
            var race=await Task.WhenAll(Json(first,"/api/booking/create",Body("Client One","10:00")),Json(second,"/api/booking/create",Body("Client Two","10:00")));
            Check(race.Count(r=>r.StatusCode==HttpStatusCode.OK)==1 && race.Count(r=>r.StatusCode==HttpStatusCode.Conflict)==1,"Concurrent same-slot requests: one success, one conflict");
            var winnerIndex=Array.FindIndex(race,r=>r.StatusCode==HttpStatusCode.OK);var winner=winnerIndex==0?first:second;var other=winnerIndex==0?second:first;
            var created=await race[winnerIndex].Content.ReadFromJsonAsync<JsonElement>();var code=created.GetProperty("bookingCode").GetString()!;
            Check(code.Length==20 && code.StartsWith("BRB-"),"Cryptographic random booking locator");
            response=await other.GetAsync("/Booking/Confirmation?code="+code);Check(response.StatusCode==HttpStatusCode.NotFound,"Customer cannot read another customer's ticket");
            response=await anonymous.GetAsync("/Booking/Confirmation?code="+code);Check(response.StatusCode==HttpStatusCode.NotFound,"Booking code alone reveals no customer data");
            response=await anonymous.GetAsync("/Booking/Track?phone=%2B996555123456&code="+code);var html=await response.Content.ReadAsStringAsync();Check(!html.Contains("Client One")&&!html.Contains("Client Two")&&!html.Contains("+996555123456"),"Legacy phone/code lookup does not disclose bookings");
            response=await winner.GetAsync("/Booking/Confirmation?code="+code);html=await response.Content.ReadAsStringAsync();Check(response.IsSuccessStatusCode&&html.Contains("800")&&!html.Contains("1 сом"),"Server uses catalog price, ignoring injected price/customer ID");
            Appointment appointment;
            using(var scope=app.Services.CreateScope())appointment=await scope.ServiceProvider.GetRequiredService<BarberDbContext>().Appointments.AsNoTracking().SingleAsync(a=>a.BookingCode==code);
            response=await Form(other,"/Account/MyBookings","/Booking/Cancel",new(){["code"]=code,["version"]=appointment.Version.ToString(),["reason"]="tamper"});
            using(var scope=app.Services.CreateScope())Check((await scope.ServiceProvider.GetRequiredService<BarberDbContext>().Appointments.AsNoTracking().SingleAsync(a=>a.Id==appointment.Id)).Status==AppointmentStatus.Pending,"IDOR cancellation cannot alter another customer's appointment");
            response=await owner.PostAsync("/admin/update-status",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",appointment.Id.ToString()},{"status","1"},{"version",appointment.Version.ToString()}}));Check(response.StatusCode==HttpStatusCode.BadRequest,"Staff status change without CSRF rejected");
            await Form(owner,"/admin/timeline","/admin/update-status",new(){["id"]=appointment.Id.ToString(),["status"]="1",["version"]=appointment.Version.ToString()});
            using(var scope=app.Services.CreateScope())appointment=await scope.ServiceProvider.GetRequiredService<BarberDbContext>().Appointments.AsNoTracking().SingleAsync(a=>a.Id==appointment.Id);
            Check(appointment.Status==AppointmentStatus.Confirmed,"Pending appointment can be confirmed by staff");
            await Form(owner,"/admin/timeline","/admin/update-status",new(){["id"]=appointment.Id.ToString(),["status"]="2",["version"]=appointment.Version.ToString()});
            using(var scope=app.Services.CreateScope())Check((await scope.ServiceProvider.GetRequiredService<BarberDbContext>().Appointments.AsNoTracking().SingleAsync(a=>a.Id==appointment.Id)).Status==AppointmentStatus.Confirmed,"Future visit cannot be marked completed");
            response=await Json(first,"/api/booking/create",Body("Invalid grid","10:15"));Check(response.StatusCode==HttpStatusCode.BadRequest,"Forged off-grid slot rejected");
            response=await Json(first,"/api/booking/create",Body("Past date","12:00",day:tomorrow.AddDays(-10)));Check(response.StatusCode==HttpStatusCode.BadRequest,"Past date rejected by server");
            await Form(owner,"/admin/time-off","/admin/time-off",new(){["barberId"]="1",["date"]=tomorrow.ToString("yyyy-MM-dd"),["start"]="13:00",["end"]="14:00",["reason"]="Private break"});
            response=await Json(first,"/api/booking/create",Body("Blocked by break","13:00"));Check(response.StatusCode==HttpStatusCode.Conflict,"Time off blocks booking at transaction commit");
            response=await anonymous.GetAsync($"/api/booking/slots?barberId=1&serviceId=1&date={tomorrow:yyyy-MM-dd}");html=await response.Content.ReadAsStringAsync();Check(!html.Contains("Private break")&&!html.Contains("Client One")&&!html.Contains("+996"),"Public availability contains no contact or time-off reason");
            await using(var command=sql.CreateCommand()) {command.CommandText="SELECT ClientName,ClientPhone,Notes FROM Appointments LIMIT 1";await using var reader=await command.ExecuteReaderAsync();await reader.ReadAsync();Check(reader.GetString(0).StartsWith("enc1:")&&reader.GetString(1).StartsWith("enc1:")&&reader.GetString(2).StartsWith("enc1:"),"MySQL contains ciphertext for name, phone and notes");}
            response=await owner.GetAsync("/admin/timeline?date="+tomorrow.ToString("yyyy-MM-dd"));html=await response.Content.ReadAsStringAsync();Check(!html.Contains("onclick=")&&!html.Contains("<img src=x onerror=alert(1)>"),"Schedule uses encoded data and no inline event handlers");
            Check(response.Headers.TryGetValues("Content-Security-Policy",out var policies)&&policies.Single().Contains("script-src 'self' 'nonce-")&&!policies.Single().Contains("script-src 'self' 'unsafe-inline'"),"Nonce-based script CSP enforced");
            response=await Json(first,"/api/booking/create",Body("Second barber client","11:00",2));Check(response.IsSuccessStatusCode,"Different barber has independent availability");
            var staffPassword="Temp_A1_"+Guid.NewGuid().ToString("N");
            response=await Form(owner,"/admin/team","/admin/team/create",new(){["UserName"]="testbarber",["DisplayName"]="Test Barber",["Password"]=staffPassword,["Role"]=Roles.Barber,["BarberId"]="1"});Check(response.StatusCode==HttpStatusCode.Redirect,"Owner creates a scoped barber account");
            using var barber=Client(app); await Login(barber,"testbarber",staffPassword);response=await barber.GetAsync("/admin/timeline");Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.ToString().Contains("ChangePassword"),"Temporary staff password must be replaced");
            response=await Form(barber,"/Account/ChangePassword","/Account/ChangePassword",new(){["CurrentPassword"]=staffPassword,["NewPassword"]=password,["ConfirmPassword"]=password});Check(response.StatusCode==HttpStatusCode.Redirect,"Staff replaces temporary password");
            response=await barber.GetAsync("/admin/timeline?date="+tomorrow.ToString("yyyy-MM-dd"));html=await response.Content.ReadAsStringAsync();
            var visibleVisits=Regex.Matches(html,"data-appointment=\"([^\"]+)\"").Select(m=>JsonDocument.Parse(WebUtility.HtmlDecode(m.Groups[1].Value))).ToArray();
            Check(response.IsSuccessStatusCode && visibleVisits.Length>0 && !html.Contains("Second barber client") && !html.Contains("+996555123456")
                && visibleVisits.All(v=>v.RootElement.GetProperty("clientPhone").GetString()!.StartsWith("•••")),"Barber sees only assigned schedule and masked contact");
            foreach(var visit in visibleVisits)visit.Dispose();
            response=await barber.GetAsync("/admin/team");Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.ToString().Contains("Denied"),"Barber cannot manage accounts");
            response=await owner.GetAsync("/admin/settings");html=await response.Content.ReadAsStringAsync();
            var hostSecrets=vault.ReadSecrets();
            Check(new[]{hostSecrets.DatabaseAdminPassword,hostSecrets.DatabaseUserPassword,new MySqlConnectionStringBuilder(hostSecrets.ConnectionString).Password}
                .Where(s=>!string.IsNullOrEmpty(s)).All(s=>!html.Contains(s))&&!html.Contains("secrets.vault"),"Server/database secrets absent from owner HTML");
            await Form(first,"/Account/Security","/Account/BeginTwoFactor",new(){["password"]=password});
            string key;
            using(var scope=app.Services.CreateScope()){var manager=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();key=(await manager.GetAuthenticatorKeyAsync((await manager.FindByNameAsync("customer_one"))!))!;}
            Check(!string.IsNullOrEmpty(key),"TOTP enrollment uses an account-specific key");
            response=await Form(first,"/Account/EnrollTwoFactor","/Account/EnableTwoFactor",new(){["Code"]=Totp(key)});Check(response.StatusCode==HttpStatusCode.Redirect,"Real TOTP code enables second factor");
            response=await first.GetAsync("/Account/Security");html=await response.Content.ReadAsStringAsync();var codes=Regex.Match(html,"<pre>([\\s\\S]*?)</pre>");Check(codes.Success&&WebUtility.HtmlDecode(codes.Groups[1].Value).Split('\n',StringSplitOptions.RemoveEmptyEntries).Length==10,"Ten recovery codes shown once");
            await Form(first,"/Account/Security","/Account/Logout",new());
            response=await Login(first,"customer_one",password);Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.ToString().Contains("TwoFactor"),"Password alone cannot complete MFA login");
            response=await Form(first,"/Account/TwoFactor","/Account/TwoFactor",new(){["Code"]=Totp(key)});Check(response.StatusCode==HttpStatusCode.Redirect,"Second factor completes login");
            string customerId;
            using(var scope=app.Services.CreateScope())customerId=(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync("customer_one"))!.Id;
            await Form(owner,"/admin/team","/admin/team/toggle",new(){["id"]=customerId});
            response=await first.GetAsync("/Account/MyBookings");Check(response.StatusCode==HttpStatusCode.Redirect&&response.Headers.Location!.ToString().Contains("Login"),"Disabled account loses an existing session immediately");
            await using(var command=sql.CreateCommand()){command.CommandText="SELECT Value FROM AspNetUserTokens LIMIT 1";var stored=Convert.ToString(await command.ExecuteScalarAsync());Check(stored?.StartsWith("enc1:")==true,"Authenticator/recovery token material encrypted in MySQL");}
            foreach(var path in new[]{"/server.json","/secrets.vault","/master.key","/appsettings.json"}){response=await anonymous.GetAsync(path);Check(response.StatusCode==HttpStatusCode.NotFound,"Private config not served: "+path);}
        }
        finally{await File.WriteAllTextAsync(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new {passed=passed.Count,checks=passed},new JsonSerializerOptions{WriteIndented=true}));}
    }

    private static string Totp(string secret)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var bytes=new List<byte>();int bits=0,value=0;
        foreach(var character in secret.ToUpperInvariant()){var digit=alphabet.IndexOf(character);if(digit<0)continue;value=(value<<5)|digit;bits+=5;if(bits>=8){bits-=8;bytes.Add((byte)(value>>bits));}}
        var counter=BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30);if(BitConverter.IsLittleEndian)Array.Reverse(counter);
        var hash=HMACSHA1.HashData(bytes.ToArray(),counter);var offset=hash[^1]&15;var binary=((hash[offset]&127)<<24)|(hash[offset+1]<<16)|(hash[offset+2]<<8)|hash[offset+3];return(binary%1_000_000).ToString("D6",CultureInfo.InvariantCulture);
    }
}
