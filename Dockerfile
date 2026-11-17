# Not verified locally: Docker is not installed on the machine this was built on.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Helpdesk.Web/Helpdesk.Web.csproj src/Helpdesk.Web/
RUN dotnet restore src/Helpdesk.Web/Helpdesk.Web.csproj
COPY src/Helpdesk.Web/ src/Helpdesk.Web/
RUN dotnet publish src/Helpdesk.Web/Helpdesk.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Helpdesk.Web.dll"]
