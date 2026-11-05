FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props .editorconfig ./
COPY src/FeedbackRouter.Core/FeedbackRouter.Core.csproj src/FeedbackRouter.Core/
COPY src/FeedbackRouter.Infra/FeedbackRouter.Infra.csproj src/FeedbackRouter.Infra/
COPY src/FeedbackRouter.Api/FeedbackRouter.Api.csproj src/FeedbackRouter.Api/
RUN dotnet restore src/FeedbackRouter.Api
COPY src/FeedbackRouter.Core/ src/FeedbackRouter.Core/
COPY src/FeedbackRouter.Infra/ src/FeedbackRouter.Infra/
COPY src/FeedbackRouter.Api/ src/FeedbackRouter.Api/
RUN dotnet publish src/FeedbackRouter.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "FeedbackRouter.Api.dll"]
