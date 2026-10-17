// Run only against a disposable QA profile. Input JSON is read from stdin, never CLI arguments.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const puppeteer = require(process.env.BLADEZ_PUPPETEER_MODULE || 'puppeteer');

async function main() {
  const config = JSON.parse(fs.readFileSync(0, 'utf8'));
  fs.mkdirSync(config.output, {recursive: true});
  const browser = await puppeteer.launch({headless: true, executablePath: process.env.BLADEZ_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe', args: ['--no-first-run', '--disable-extensions']});
  const errors = [], external = [], screenshots = [];
  const page = await browser.newPage();
  await page.setViewport({width: 1440, height: 1000, deviceScaleFactor: 1});
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
  page.on('request', request => { if (!request.url().startsWith(config.url) && !request.url().startsWith('data:')) external.push(request.url()); });
  page.on('dialog', dialog => dialog.accept());
  const go = async url => { const response = await page.goto(config.url + url, {waitUntil:'networkidle0'}); if(response.status()>=400) throw new Error(`${url}: HTTP ${response.status()}`); };
  const shot = async (name, fullPage = false) => { await page.screenshot({path:path.join(config.output,name+'.png'),fullPage}); screenshots.push(name); };
  const submit = async selector => await Promise.all([page.waitForNavigation({waitUntil:'networkidle0'}),page.click(selector)]);
  const fill = async (selector, value) => { await page.$eval(selector, input => {input.value='';}); await page.type(selector,value); };
  try {
    await go('/'); await shot('01-home'); await shot('02-home-full',true);
    await go('/Account/Login'); await shot('03-login');
    await go('/Account/Register'); await shot('04-register',true);
    const customerPassword = 'QaCustomer_A1_'+crypto.randomBytes(12).toString('hex');
    await fill('#DisplayName','Тимур · тестовый клиент'); await fill('#UserName','qa_'+crypto.randomBytes(6).toString('hex'));
    await fill('#Password',customerPassword); await fill('#ConfirmPassword',customerPassword); await page.click('#AcceptPrivacy');
    await submit('form[action="/Account/Register"] button');
    if(!page.url().includes('MyBookings')) throw new Error('Customer registration did not finish.');
    await go('/Booking?serviceId=1&barberId=1');
    if(await page.$eval('#btnStep1Next',button=>button.disabled)) throw new Error('Preselected service was not applied.');
    await shot('05-booking-service'); await page.click('#btnStep1Next'); await shot('06-booking-barber');
    await page.click('#btnStep2Next');
    const date = new Date(Date.now()+86400000); while(new Intl.DateTimeFormat('en-US',{timeZone:'Asia/Bishkek',weekday:'short'}).format(date)==='Sun')date.setDate(date.getDate()+1);
    const day = new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Bishkek',year:'numeric',month:'2-digit',day:'2-digit'}).format(date);
    await page.$eval('#bookingDateInput',(input,value)=>{input.value=value;input.dispatchEvent(new Event('change',{bubbles:true}));},day);
    await page.waitForSelector('.time-slot-btn:not(:disabled)'); await page.click('.time-slot-btn:not(:disabled)'); await shot('07-booking-time');
    await page.click('#btnStep3Next'); await fill('#clientName','Тимур · тестовый клиент'); await fill('#clientPhone','+996555000001');
    await fill('#clientNotes','Демонстрационная запись для проверки интерфейса.'); await shot('08-booking-contacts');
    await submit('#btnSubmitBooking');
    if(!page.url().includes('Confirmation')) throw new Error('Booking wizard failed: '+await page.$eval('#bookingErrorMessage',element=>element.textContent).catch(()=>page.url()));
    await shot('09-ticket'); await go('/Account/MyBookings'); await shot('10-my-bookings');
    await go('/Account/Profile'); await shot('11-profile'); await go('/Account/Security'); await shot('12-security');
    for(const [url,name] of [['/','13-mobile-home'],['/Account/MyBookings','14-mobile-bookings'],['/Account/Security','15-mobile-security']]) {
      await page.setViewport({width:390,height:844,deviceScaleFactor:1}); await go(url); await shot(name,true);
      const overflow = await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1);
      if(overflow)throw new Error('Horizontal viewport overflow at '+url);
    }
    await page.setViewport({width:1440,height:1000,deviceScaleFactor:1});
    await submit('form[action="/Account/Logout"] button'); await go('/Account/Login');
    await fill('#UserName',config.owner); await fill('#Password',config.password); await submit('form[action="/Account/Login"] button');
    await go('/admin/timeline?date='+day);
    const clipped = await page.evaluate(()=>Array.from(document.querySelectorAll('.appointment-block.compact')).some(card=> {
      const bounds=card.getBoundingClientRect();
      return ['.slot-time','strong'].some(selector=> {const text=card.querySelector(selector).getBoundingClientRect(); return text.height<10 || text.bottom>bounds.bottom-2 || text.top<bounds.top;});
    }));
    if(clipped)throw new Error('Short appointment text is vertically clipped');
    await shot('16-timeline');
    await page.click('[data-appointment]'); await page.waitForSelector('#appointmentDetails.show'); await new Promise(resolve=>setTimeout(resolve,350)); await shot('17-appointment-details');
    await page.click('#appointmentDetails .btn-close'); await page.waitForFunction(()=>!document.querySelector('#appointmentDetails').classList.contains('show') && !document.querySelector('.modal-backdrop'));
    await page.click('[data-bs-target="#quickBooking"]'); await page.waitForSelector('#quickBooking.show'); await new Promise(resolve=>setTimeout(resolve,350)); await shot('18-quick-booking');
    for(const [url,name] of [['/admin/dashboard','19-dashboard'],['/admin/appointments','20-appointments'],['/admin/services','21-services'],['/admin/barbers','22-barbers'],['/admin/team','23-team'],['/admin/time-off','24-time-off'],['/admin/settings','25-settings'],['/admin/audit','26-audit'],['/admin/system','27-system']]) {
      await go(url); await shot(name,url==='/admin/settings');
      if(!await page.$('h1'))throw new Error('Missing page heading: '+url);
    }
    await page.setViewport({width:390,height:844,deviceScaleFactor:1}); await go('/admin/timeline?date='+day); await shot('28-mobile-timeline',true);
    if(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1))throw new Error('Admin mobile viewport overflow');
    await page.setViewport({width:1440,height:1000,deviceScaleFactor:1});
    await go('/Account/Security');
    if(await page.evaluate(()=>performance.getEntriesByType('resource').some(item=>!item.name.startsWith(location.origin))))throw new Error('External app resource');
    if(errors.length || external.length)throw new Error(JSON.stringify({errors,external}));
    fs.writeFileSync(path.join(config.output,'browser-report.json'),JSON.stringify({screenshots,errors,external,checks:['customer registration','catalog preselection','keyboard-ready slots','full booking wizard','private ticket','customer history','mobile 390px','owner workspace','strict CSP and local assets']},null,2));
    console.log('PASS: browser flow, '+screenshots.length+' actual screenshots, no JavaScript errors/external requests');
  } finally { await browser.close(); }
}
main().catch(error=>{console.error(error);process.exitCode=1;});
