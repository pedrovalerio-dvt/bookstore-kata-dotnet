FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Bookstore.Core/Bookstore.Core.csproj src/Bookstore.Core/
COPY src/Bookstore.Infrastructure/Bookstore.Infrastructure.csproj src/Bookstore.Infrastructure/
COPY src/Bookstore.Api/Bookstore.Api.csproj src/Bookstore.Api/
RUN dotnet restore src/Bookstore.Api/Bookstore.Api.csproj
COPY src/Bookstore.Core/ src/Bookstore.Core/
COPY src/Bookstore.Infrastructure/ src/Bookstore.Infrastructure/
COPY src/Bookstore.Api/ src/Bookstore.Api/
RUN dotnet publish src/Bookstore.Api/Bookstore.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bookstore.Api.dll"]
