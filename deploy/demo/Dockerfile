# The public demo in one container: the API (with the dashboard in wwwroot) and the sample-shop generator side by
# side, so a single free web service (Render) runs the whole thing next to a hosted PostgreSQL (Neon).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props LogAnalytics.slnx ./
COPY src/ src/
RUN dotnet publish src/LogAnalytics.Api/LogAnalytics.Api.csproj -c Release -o /app/api /p:UseAppHost=false \
 && dotnet publish src/LogAnalytics.Generator/LogAnalytics.Generator.csproj -c Release -o /app/generator /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/api ./api
COPY --from=build /app/generator ./generator
COPY deploy/demo/start.sh ./start.sh
USER $APP_UID
ENTRYPOINT ["sh", "/app/start.sh"]
