FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Ruumly.Backend/Ruumly.Backend.csproj", "Ruumly.Backend/"]
RUN dotnet restore "Ruumly.Backend/Ruumly.Backend.csproj"

COPY . .
WORKDIR "/src/Ruumly.Backend"
RUN dotnet publish "Ruumly.Backend.csproj" -c Release \
    -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV PORT=8080
ENTRYPOINT ["dotnet", "Ruumly.Backend.dll"]
