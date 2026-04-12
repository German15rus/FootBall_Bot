# ── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/PremierLeagueBot/PremierLeagueBot.csproj", "src/PremierLeagueBot/"]
RUN dotnet restore "src/PremierLeagueBot/PremierLeagueBot.csproj"

COPY . .
WORKDIR "/src/src/PremierLeagueBot"
RUN dotnet publish "PremierLeagueBot.csproj" -c Release -o /app/publish

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Port that ASP.NET Core listens on inside the container
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Copy published output
COPY --from=build /app/publish .

# Create a directory for SQLite database and logs (mount a volume here in prod)
RUN mkdir -p /app/data /app/logs

ENTRYPOINT ["dotnet", "PremierLeagueBot.dll"]
