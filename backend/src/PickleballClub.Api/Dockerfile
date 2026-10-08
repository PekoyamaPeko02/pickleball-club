FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/PickleballClub.Api/PickleballClub.Api.csproj src/PickleballClub.Api/
RUN dotnet restore src/PickleballClub.Api/PickleballClub.Api.csproj
COPY src/ src/
RUN dotnet publish src/PickleballClub.Api/PickleballClub.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "PickleballClub.Api.dll"]
