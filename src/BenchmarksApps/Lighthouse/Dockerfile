FROM mcr.microsoft.com/dotnet/nightly/sdk:latest AS build-env
RUN dotnet publish -c Release -o /publish

FROM node:18-bullseye-slim

# Install utilities
RUN apt-get update --fix-missing && apt-get -y upgrade && apt-get install -y git wget gnupg && apt-get clean

# Install latest chrome stable package
RUN wget -q -O - https://dl-ssl.google.com/linux/linux_signing_key.pub | apt-key add -
RUN sh -c 'echo "deb [arch=amd64] http://dl.google.com/linux/chrome/deb/ stable main" >> /etc/apt/sources.list.d/google.list'
RUN apt-get update \
    && apt-get install -y google-chrome-stable --no-install-recommends \
    && apt-get clean

# Install Lighthouse
RUN npm install -g lighthouse

RUN lighthouse http://localhost:5000 \
    --no-enable-error-reporting \
    --output=json \
    --output-path=./report.json \
    --only-audits="first-contentful-paint,interaction-to-next-paint,largest-contentful-paint,total-blocking-time"

COPY --from=build-env /publish /publish

ENTRYPOINT ./publish/Lighthouse ./report.json
