FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Mcp.SqlServer/Mcp.SqlServer.csproj", "Mcp.SqlServer/"]
RUN dotnet restore "Mcp.SqlServer/Mcp.SqlServer.csproj"

COPY . .
WORKDIR "/src/Mcp.SqlServer"
RUN dotnet publish "Mcp.SqlServer.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=80

COPY --from=build /app/publish .

EXPOSE 80

ENTRYPOINT ["dotnet", "Mcp.SqlServer.dll"]