# Contexto de build: raiz do repositório (usa Directory.Build.props e .editorconfig).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY Directory.Build.props .editorconfig ./
COPY sources/ExpenseHub.Api/ExpenseHub.Api.csproj sources/ExpenseHub.Api/
RUN dotnet restore sources/ExpenseHub.Api/ExpenseHub.Api.csproj
COPY sources/ExpenseHub.Api/ sources/ExpenseHub.Api/
RUN dotnet publish sources/ExpenseHub.Api/ExpenseHub.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="expensehub-web" \
      org.opencontainers.image.revision="${GIT_SHA}"
ENV ASPNETCORE_HTTP_PORTS=8080 \
    EXPENSEHUB_GIT_SHA=${GIT_SHA}
WORKDIR /app
RUN mkdir -p /data/keys && chown -R app:app /data
COPY --from=build /app .
USER app
EXPOSE 8080
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "ExpenseHub.Api.dll"]
