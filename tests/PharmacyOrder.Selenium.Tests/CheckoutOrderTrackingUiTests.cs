using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace PharmacyOrder.Selenium.Tests;

/// <summary>
/// GCN-31 checkout & order tracking — Selenium browser tests (Chrome).
///
/// Prerequisites (all must be running before `dotnet test`):
///   - frontend:              npm run dev            -> http://localhost:3000
///   - user auth service:     dotnet run             -> http://localhost:5133
///   - pharmacy order service dotnet run             -> http://localhost:5134
///
/// Configuration (environment variables, defaults in brackets):
///   CARENOVA_UI_URL          [http://localhost:3000]
///   CARENOVA_PATIENT_EMAIL   [patient@carenova.test]   an existing Patient account
///   CARENOVA_PATIENT_PASSWORD[Patient@123]
///   CARENOVA_HEADLESS        [true]  set to "false" to watch the browser
/// </summary>
public class CheckoutOrderTrackingUiTests : IDisposable
{
    private static readonly string BaseUrl = Env("CARENOVA_UI_URL", "http://localhost:3000").TrimEnd('/');
    private static readonly string PatientEmail = Env("CARENOVA_PATIENT_EMAIL", "patient@carenova.test");
    private static readonly string PatientPassword = Env("CARENOVA_PATIENT_PASSWORD", "Patient@123");

    private readonly IWebDriver driver;
    private readonly WebDriverWait wait;

    public CheckoutOrderTrackingUiTests()
    {
        var options = new ChromeOptions();
        if (!string.Equals(Env("CARENOVA_HEADLESS", "true"), "false", StringComparison.OrdinalIgnoreCase))
            options.AddArgument("--headless=new");
        options.AddArgument("--window-size=1366,900");
        options.AddArgument("--disable-gpu");
        driver = new ChromeDriver(options);
        wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
    }

    public void Dispose() => driver.Quit();

    // 1 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void Login_page_renders_email_and_password_fields()
    {
        driver.Navigate().GoToUrl($"{BaseUrl}/login");

        var email = wait.Until(d => d.FindElement(By.Id("login-email")));
        var password = driver.FindElement(By.Id("login-password"));
        var submit = driver.FindElement(By.CssSelector("button[type='submit']"));

        Assert.True(email.Displayed);
        Assert.True(password.Displayed);
        Assert.True(submit.Displayed);
        Assert.Equal("email", email.GetDomAttribute("type"));
        Assert.Equal("password", password.GetDomAttribute("type"));
    }

    // 2 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void Invalid_credentials_stay_on_login_page_and_show_feedback()
    {
        driver.Navigate().GoToUrl($"{BaseUrl}/login");
        Login("nobody@carenova.test", "WrongPassword!1");

        // Either an inline error/alert appears or we are still on /login — both mean login was refused.
        wait.Until(d => d.Url.Contains("/login") &&
            (d.FindElements(By.CssSelector("[role='alert'], .error, .form-error, .login-error")).Count > 0
             || d.FindElement(By.Id("login-email")).Displayed));

        Assert.Contains("/login", driver.Url);
        Assert.DoesNotContain("/patient-dashboard", driver.Url);
    }

    // 3 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void Patient_can_log_in_and_open_order_history()
    {
        driver.Navigate().GoToUrl($"{BaseUrl}/login");
        Login(PatientEmail, PatientPassword);
        wait.Until(d => !d.Url.Contains("/login"));

        driver.Navigate().GoToUrl($"{BaseUrl}/patient/orders");

        var heading = wait.Until(d => d.FindElement(By.XPath("//h1[normalize-space()='My orders']")));
        Assert.True(heading.Displayed);
        Assert.True(driver.FindElement(By.XPath("//button[normalize-space()='Refresh orders']")).Displayed);
        // Every order card, if any, must link to its details page and show a status pill.
        foreach (var card in driver.FindElements(By.CssSelector("article.order-card")))
        {
            Assert.Matches(@"^CN-\d{6}$", card.FindElement(By.CssSelector("h2 a")).Text.Trim());
            Assert.True(card.FindElement(By.CssSelector(".order-status")).Displayed);
        }
    }

    // 4 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void Patient_checkout_page_shows_review_or_empty_state_with_pickup_info()
    {
        driver.Navigate().GoToUrl($"{BaseUrl}/login");
        Login(PatientEmail, PatientPassword);
        wait.Until(d => !d.Url.Contains("/login"));

        driver.Navigate().GoToUrl($"{BaseUrl}/patient/checkout");

        var heading = wait.Until(d => d.FindElement(By.XPath("//h1[normalize-space()='Review your order']")));
        Assert.True(heading.Displayed);

        // Wait until the review request has settled: either ready items or the empty-state message.
        wait.Until(d =>
            d.FindElements(By.XPath("//button[normalize-space()='Confirm and place order']")).Count > 0 ||
            d.FindElements(By.XPath("//p[normalize-space()='No medicines are ready to order yet.']")).Count > 0);

        var hasItems = driver.FindElements(By.XPath("//button[normalize-space()='Confirm and place order']")).Count > 0;
        if (hasItems)
        {
            Assert.True(driver.FindElement(By.XPath("//strong[normalize-space()='Pharmacy pickup']")).Displayed);
            Assert.Contains("Pending", driver.FindElement(By.CssSelector(".order-summary p")).Text);
            Assert.StartsWith("LKR ", driver.FindElement(By.CssSelector(".order-total strong")).Text);
        }
        else
        {
            Assert.True(driver.FindElement(By.XPath("//p[normalize-space()='No medicines are ready to order yet.']")).Displayed);
        }
        Assert.True(driver.FindElement(By.XPath("//a[normalize-space()='Back to cart']")).Displayed);
    }

    // ───────────────────────────────────────────────────────────────────────

    private void Login(string email, string password)
    {
        var emailField = wait.Until(d => d.FindElement(By.Id("login-email")));
        emailField.Clear();
        emailField.SendKeys(email);
        var passwordField = driver.FindElement(By.Id("login-password"));
        passwordField.Clear();
        passwordField.SendKeys(password);
        driver.FindElement(By.CssSelector("button[type='submit']")).Click();
    }

    private static string Env(string name, string fallback) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? fallback : Environment.GetEnvironmentVariable(name)!;
}
