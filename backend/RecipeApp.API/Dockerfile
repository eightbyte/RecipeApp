# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY RecipeApp.API.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Download the Chromium build used by RecipeScrapeService's headless-render fallback.
# `PowerShell` ships as a plain dotnet global tool, so this needs no OS-level pwsh install.
# Only the browser binary is fetched here — its OS-level shared-lib dependencies are
# installed directly in the runtime stage below, since that's the image that runs it.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN dotnet tool install --global PowerShell \
    && export PATH="$PATH:/root/.dotnet/tools" \
    && pwsh /app/publish/playwright.ps1 install chromium

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Chromium's runtime shared-library dependencies (headless recipe-scraping fallback).
# This exact package list — including the Ubuntu 24.04 "t64" renames — is what
# `playwright install-deps chromium` resolves to on this base image; verified directly
# against mcr.microsoft.com/dotnet/aspnet:10.0 rather than hand-guessed.
RUN apt-get update && apt-get install -y --no-install-recommends \
    libasound2t64 libatk-bridge2.0-0t64 libatk1.0-0t64 libatspi2.0-0t64 \
    libcairo2 libcups2t64 libdbus-1-3 libdrm2 libgbm1 libglib2.0-0t64 \
    libnspr4 libnss3 libpango-1.0-0 libx11-6 libxcb1 libxcomposite1 \
    libxdamage1 libxext6 libxfixes3 libxkbcommon0 libxrandr2 xvfb \
    fonts-noto-color-emoji fonts-unifont libfontconfig1 libfreetype6 \
    xfonts-cyrillic xfonts-scalable fonts-liberation fonts-ipafont-gothic \
    fonts-wqy-zenhei fonts-tlwg-loma-otf fonts-freefont-ttf \
    && rm -rf /var/lib/apt/lists/*

ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
COPY --from=build /ms-playwright /ms-playwright

# Create uploads directory for recipe images
RUN mkdir -p uploads/images

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "RecipeApp.API.dll"]
