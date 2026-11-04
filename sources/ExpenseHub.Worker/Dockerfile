# Contexto de build: raiz do repositório (usa Directory.Build.props e .editorconfig).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY Directory.Build.props .editorconfig ./
COPY sources/ExpenseHub.Api/ExpenseHub.Api.csproj sources/ExpenseHub.Api/
COPY sources/ExpenseHub.Worker/ExpenseHub.Worker.csproj sources/ExpenseHub.Worker/
RUN dotnet restore sources/ExpenseHub.Worker/ExpenseHub.Worker.csproj
COPY sources/ExpenseHub.Api/ sources/ExpenseHub.Api/
COPY sources/ExpenseHub.Worker/ sources/ExpenseHub.Worker/
RUN dotnet publish sources/ExpenseHub.Worker/ExpenseHub.Worker.csproj -c Release -o /app --no-restore -p:UseAppHost=false

# O worker referencia o projeto web (domínio, EF Core e mensageria), então usa o runtime ASP.NET.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG GIT_SHA=unknown
LABEL org.opencontainers.image.title="expensehub-worker" \
      org.opencontainers.image.revision="${GIT_SHA}"
ENV EXPENSEHUB_GIT_SHA=${GIT_SHA}
WORKDIR /app
RUN mkdir -p /data && chown -R app:app /data
COPY --from=build /app .
USER app
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "ExpenseHub.Worker.dll"]
