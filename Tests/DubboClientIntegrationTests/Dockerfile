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

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

# Official .NET 10 images already provide the non-root app user.
USER $APP_UID
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["DubboNet.csproj", "."]
COPY ["Tests/DubboClientIntegrationTests/DubboClientIntegrationTests.csproj", "Tests/DubboClientIntegrationTests/"]
COPY ["Tests/Shared/TestEndpointOptions.cs", "Tests/Shared/"]
RUN dotnet restore "Tests/DubboClientIntegrationTests/DubboClientIntegrationTests.csproj"
COPY . .
WORKDIR "/src/Tests/DubboClientIntegrationTests"
RUN dotnet build "DubboClientIntegrationTests.csproj" -c $configuration -o /app/build -p:GeneratePackageOnBuild=false

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "DubboClientIntegrationTests.csproj" -c $configuration -o /app/publish /p:UseAppHost=false -p:GeneratePackageOnBuild=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DubboClientIntegrationTests.dll"]
