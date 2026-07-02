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
COPY ["src/FinanceApp/FinanceApp.csproj", "src/FinanceApp/"]
COPY ["src/FinanceApp.Domain/FinanceApp.Domain.csproj", "src/FinanceApp.Domain/"]
COPY ["src/FinanceApp.Connectors/FinanceApp.Connectors.csproj", "src/FinanceApp.Connectors/"]
COPY ["src/FinanceApp.Categorization/FinanceApp.Categorization.csproj", "src/FinanceApp.Categorization/"]

RUN dotnet restore "src/FinanceApp/FinanceApp.csproj"

COPY src/ src/
WORKDIR "/src/src/FinanceApp"

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Do NOT use --no-restore here: Microsoft.AspNetCore.App.Internal.Assets is an
# implicit SDK package (not listed in .csproj) that provides _framework/blazor.web.js
# via the static-web-assets manifest. The csproj-only restore layer above won't
# fetch it; publish must be allowed to restore so the Blazor SDK task can include it.
RUN dotnet publish "FinanceApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FinanceApp.dll"]
