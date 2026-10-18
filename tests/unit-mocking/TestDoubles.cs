using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using PharmacyOrder.Api.Data;

namespace PharmacyOrder.UnitMocking.Tests;

// shared test doubles
internal static class TestDoubles
{
    // fake database: in-memory instead of MySQL
    public static AppDbContext FakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    // logged in user for controller tests
    public static T WithUser<T>(this T controller, Guid userId) where T : ControllerBase
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "Test");
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return controller;
    }

    // mock uploaded file
    public static IFormFile File(string fileName, long length)
    {
        var file = new Mock<IFormFile>();
        file.Setup(f => f.FileName).Returns(fileName);
        file.Setup(f => f.Length).Returns(length);
        return file.Object;
    }
}
