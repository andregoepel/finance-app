global using AndreGoepel.Testing.E2E;
global using Microsoft.Playwright;
global using static Microsoft.Playwright.Assertions;
global using Xunit;
// AndreGoepel.Testing.E2E and this repo's own Infrastructure namespace both declare an
// E2EAppFixture (the local one subclasses the package's, per the package's documented pattern) —
// alias the bare name to the local subclass everywhere so test classes don't need to fully
// qualify it.
global using E2EAppFixture = AndreGoepel.FinanceApp.E2ETests.Infrastructure.E2EAppFixture;
