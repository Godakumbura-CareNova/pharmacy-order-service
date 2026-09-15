using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace PharmacyOrder.Selenium.Tests;

/// <summary>
/// GCN-32 pharmacy order review — Selenium browser tests (Chrome).
///
/// Prerequisites (all must be running before `dotnet test`):
///   - frontend:               npm run dev                                   -> http://localhost:3000
///   - user auth service:      dotnet run --project src/UserAuth.Api          -> http://localhost:5133
///   - pharmacy order service: dotnet run --project src/PharmacyOrder.Api     -> http://localhost:5134
///
/// Configuration (environment variables, defaults in brackets):
///   CARENOVA_UI_URL            [http://localhost:3000]
///   CARENOVA_STAFF_EMAIL       [staff@carenova.test]     an account with the PharmacyStaff role
///   CARENOVA_STAFF_PASSWORD    [Staff@123]
///   CARENOVA_PATIENT_EMAIL     [patient@carenova.test]   a Patient account (used for the access-denied check)
///   CARENOVA_PATIENT_PASSWORD  [Patient@123]
///   CARENOVA_HEADLESS          [true]  set to "false" to watch the browser
///
/// None of these tests change order or prescription state.
/// </summary>
public class PharmacyOrderReviewUiTests : IDisposable
{
    private static readonly string BaseUrl = Env("CARENOVA_UI_URL", "http://localhost:3000").TrimEnd('/');
    private static readonly string StaffEmail = Env("CARENOVA_STAFF_EMAIL", "staff@carenova.test");
    private static readonly string StaffPassword = Env("CARENOVA_STAFF_PASSWORD", "Staff@123");
    private static readonly string PatientEmail = Env("CARENOVA_PATIENT_EMAIL", "patient@carenova.test");
    private static readonly string PatientPassword = Env("CARENOVA_PATIENT_PASSWORD", "Patient@123");

    private readonly IWebDriver driver;
    private readonly WebDriverWait wait;

    public PharmacyOrderReviewUiTests()
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
    public void Staff_login_lands_on_pharmacy_dashboard_with_order_review_navigation()
    {
        LoginAs(StaffEmail, StaffPassword);

        wait.Until(d => d.Url.Contains("/pharmacy-dashboard"));
        Assert.True(wait.Until(d => d.FindElement(By.XPath("//a[contains(@href,'/pharmacy/orders')]"))).Displayed);
        Assert.True(driver.FindElement(By.XPath("//a[contains(@href,'/pharmacy/prescriptions')]")).Displayed);
    }

    // 2 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void Medicine_orders_queue_shows_only_actionable_orders_with_correct_buttons()
    {
        LoginAs(StaffEmail, StaffPassword);
        driver.Navigate().GoToUrl($"{BaseUrl}/pharmacy/orders");

        Assert.True(wait.Until(d => d.FindElement(By.XPath("//h1[normalize-space()='Medicine orders']"))).Displayed);
        wait.Until(d =>
            d.FindElements(By.CssSelector("article.staff-order-card")).Count > 0 ||
            d.FindElements(By.XPath("//p[normalize-space()='No orders need pharmacy action.']")).Count > 0);

        foreach (var card in driver.FindElements(By.CssSelector("article.staff-order-card")))
        {
            var status = card.FindElement(By.CssSelector(".staff-order-status")).Text.Trim();
            Assert.Matches(@"^CN-\d{6}$", card.FindElement(By.TagName("h2")).Text.Trim());
            Assert.StartsWith("LKR ", card.FindElement(By.CssSelector(".staff-order-total")).Text);
            Assert.Contains(status, new[] { "Pending", "ReadyForCollection" });

            var ready = card.FindElements(By.XPath(".//button[normalize-space()='Mark ready for collection']")).Count;
            var reject = card.FindElements(By.XPath(".//button[normalize-space()='Reject order']")).Count;
            var collected = card.FindElements(By.XPath(".//button[normalize-space()='Mark collected']")).Count;
            if (status == "Pending") { Assert.Equal(1, ready); Assert.Equal(1, reject); Assert.Equal(0, collected); }
            else { Assert.Equal(0, ready); Assert.Equal(0, reject); Assert.Equal(1, collected); }
        }
    }

    // 3 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void Rejecting_an_order_requires_a_reason_and_can_be_cancelled()
    {
        LoginAs(StaffEmail, StaffPassword);
        driver.Navigate().GoToUrl($"{BaseUrl}/pharmacy/orders");
        wait.Until(d => d.FindElement(By.XPath("//h1[normalize-space()='Medicine orders']")));
        wait.Until(d =>
            d.FindElements(By.CssSelector("article.staff-order-card")).Count > 0 ||
            d.FindElements(By.XPath("//p[normalize-space()='No orders need pharmacy action.']")).Count > 0);

        var rejectButtons = driver.FindElements(By.XPath("//button[normalize-space()='Reject order']"));
        if (rejectButtons.Count == 0)
        {
            // No pending order on this environment: the page rendered its empty/ready-only state correctly.
            Assert.True(driver.FindElements(By.XPath("//button[normalize-space()='Mark collected']")).Count > 0
                     || driver.FindElement(By.XPath("//p[normalize-space()='No orders need pharmacy action.']")).Displayed);
            return;
        }

        rejectButtons[0].Click();
        var reasonInput = wait.Until(d => d.FindElement(By.XPath("//label[normalize-space()='Rejection reason']/following-sibling::input")));
        Assert.True(reasonInput.Displayed);

        driver.FindElement(By.XPath("//button[normalize-space()='Confirm rejection']")).Click();
        var alert = wait.Until(d => d.FindElement(By.XPath("//p[@role='alert' and normalize-space()='Provide a rejection reason.']")));
        Assert.True(alert.Displayed);

        driver.FindElement(By.XPath("//button[normalize-space()='Cancel']")).Click();
        wait.Until(d => d.FindElements(By.XPath("//label[normalize-space()='Rejection reason']")).Count == 0);
        // Order is untouched: the reject button is still offered.
        Assert.True(driver.FindElements(By.XPath("//button[normalize-space()='Reject order']")).Count > 0);
    }

    // 4 ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void Prescription_review_page_loads_for_staff_and_is_forbidden_for_patients()
    {
        LoginAs(StaffEmail, StaffPassword);
        driver.Navigate().GoToUrl($"{BaseUrl}/pharmacy/prescriptions");
        Assert.True(wait.Until(d => d.FindElement(By.XPath("//h1[normalize-space()='Prescription review']"))).Displayed);
        wait.Until(d =>
            d.FindElements(By.CssSelector("article.staff-prescription-card")).Count > 0 ||
            d.FindElements(By.XPath("//p[normalize-space()='No prescriptions are waiting for verification.']")).Count > 0);
        foreach (var card in driver.FindElements(By.CssSelector("article.staff-prescription-card")))
        {
            Assert.Equal("Pending verification", card.FindElement(By.CssSelector(".staff-prescription-status")).Text.Trim());
            Assert.True(card.FindElement(By.XPath(".//button[@aria-label='Verify prescription']")).Displayed);
            Assert.True(card.FindElement(By.XPath(".//button[normalize-space()='Reject']")).Displayed);
        }

        // A patient must not reach the staff workspace.
        driver.Manage().Cookies.DeleteAllCookies();
        ((IJavaScriptExecutor)driver).ExecuteScript("localStorage.clear(); sessionStorage.clear();");
        LoginAs(PatientEmail, PatientPassword);
        driver.Navigate().GoToUrl($"{BaseUrl}/pharmacy/orders");
        wait.Until(d => !d.Url.EndsWith("/pharmacy/orders"));
        Assert.DoesNotContain("/pharmacy/orders", driver.Url);
        Assert.Empty(driver.FindElements(By.XPath("//h1[normalize-space()='Medicine orders']")));
    }

    // ───────────────────────────────────────────────────────────────────────

    private void LoginAs(string email, string password)
    {
        driver.Navigate().GoToUrl($"{BaseUrl}/login");
        var emailField = wait.Until(d => d.FindElement(By.Id("login-email")));
        emailField.Clear();
        emailField.SendKeys(email);
        var passwordField = driver.FindElement(By.Id("login-password"));
        passwordField.Clear();
        passwordField.SendKeys(password);
        driver.FindElement(By.CssSelector("button[type='submit']")).Click();
        wait.Until(d => !d.Url.Contains("/login"));
    }

    private static string Env(string name, string fallback) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)) ? fallback : Environment.GetEnvironmentVariable(name)!;
}
