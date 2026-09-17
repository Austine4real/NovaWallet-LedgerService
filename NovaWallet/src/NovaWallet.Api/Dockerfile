FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["src/NovaWallet.Domain/NovaWallet.Domain.csproj", "src/NovaWallet.Domain/"]
COPY ["src/NovaWallet.Application/NovaWallet.Application.csproj", "src/NovaWallet.Application/"]
COPY ["src/NovaWallet.Infrastructure/NovaWallet.Infrastructure.csproj", "src/NovaWallet.Infrastructure/"]
COPY ["src/NovaWallet.Api/NovaWallet.Api.csproj", "src/NovaWallet.Api/"]
RUN dotnet restore "src/NovaWallet.Api/NovaWallet.Api.csproj"

COPY . .
RUN dotnet publish "src/NovaWallet.Api/NovaWallet.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "NovaWallet.Api.dll"]
