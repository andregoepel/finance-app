# Build context is the repository root so that Directory.{Build,Packages}.props
# (centralised package management) and NuGet.config are visible to `dotnet restore`.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Shared restore inputs live at the repository root.
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["NuGet.config", "./"]

# The host project and its in-repo libraries. AppFoundation dependencies are
# restored from NuGet.
COPY ["src/AndreGoepel.FinanceApp/AndreGoepel.FinanceApp.csproj", "src/AndreGoepel.FinanceApp/"]
COPY ["src/AndreGoepel.FinanceApp.Domain/AndreGoepel.FinanceApp.Domain.csproj", "src/AndreGoepel.FinanceApp.Domain/"]
COPY ["src/AndreGoepel.FinanceApp.Connectors/AndreGoepel.FinanceApp.Connectors.csproj", "src/AndreGoepel.FinanceApp.Connectors/"]
COPY ["src/AndreGoepel.FinanceApp.Categorization/AndreGoepel.FinanceApp.Categorization.csproj", "src/AndreGoepel.FinanceApp.Categorization/"]

RUN dotnet restore "src/AndreGoepel.FinanceApp/AndreGoepel.FinanceApp.csproj"

COPY src/ src/
WORKDIR "/src/src/AndreGoepel.FinanceApp"

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Do NOT use --no-restore here: Microsoft.AspNetCore.App.Internal.Assets is an
# implicit SDK package (not listed in .csproj) that provides _framework/blazor.web.js
# via the static-web-assets manifest. The csproj-only restore layer above won't
# fetch it; publish must be allowed to restore so the Blazor SDK task can include it.
RUN dotnet publish "AndreGoepel.FinanceApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AndreGoepel.FinanceApp.dll"]
