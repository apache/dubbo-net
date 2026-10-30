# Licensed to the Apache Software Foundation (ASF) under one or more
# contributor license agreements. See the NOTICE file distributed with
# this work for additional information regarding copyright ownership.
# The ASF licenses this file to You under the Apache License, Version 2.0
# (the "License"); you may not use this file except in compliance with
# the License. You may obtain a copy of the License at
#
#   http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

RUN groupadd --gid 1654 app && useradd --uid 1654 --gid app --create-home app
USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["DubboNet.csproj", "."]
COPY ["Tests/DubboClientIntegrationTests/DubboClientIntegrationTests.csproj", "Tests/DubboClientIntegrationTests/"]
RUN dotnet restore "Tests/DubboClientIntegrationTests/DubboClientIntegrationTests.csproj"
COPY . .
WORKDIR "/src/Tests/DubboClientIntegrationTests"
RUN dotnet build "DubboClientIntegrationTests.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "DubboClientIntegrationTests.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DubboClientIntegrationTests.dll"]
