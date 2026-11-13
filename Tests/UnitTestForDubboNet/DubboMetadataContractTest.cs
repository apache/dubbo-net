/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements. See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using DubboNet.Clients;
using DubboNet.Clients.DataModle;
using DubboNet.Metadata;
using DubboNet.Clients.RegistryClient;
using DubboNet.Registry;
using System.Text;

namespace UnitTestForDubboNet
{
    public class DubboMetadataContractTest
    {
        [Fact]
        public void MetadataImplementationHasItsOwnNamespace_WithoutMovingPublicModels()
        {
            Assert.Equal("DubboNet.Metadata", typeof(DubboMetadataManager).Namespace);
            Assert.Equal("DubboNet.Metadata", typeof(DubboInvocationMetadataResolver).Namespace);
            Assert.Equal("DubboNet.Clients.DataModle", typeof(DubboMethodMetadata).Namespace);
            Assert.Equal("DubboNet.Clients.DataModle", typeof(DubboTypeMetadata).Namespace);
            Assert.Equal("DubboNet.Clients.DataModle", typeof(DubboMetadataException).Namespace);
        }

        [Theory]
        [InlineData(false, false, null, null, "/dubbo/", "/dubbo")]
        [InlineData(true, true, null, null, "DEFAULT_GROUP", "dubbo")]
        [InlineData(true, false, null, null, "DEFAULT_GROUP", "/dubbo")]
        [InlineData(false, false, "/custom/", null, "/custom/", "/custom")]
        [InlineData(true, true, "/naming/", "/metadata/", "naming", "metadata")]
        public void RegistryAndMetadataDefaults_StayIndependent(
            bool namingUsesNacos, bool metadataUsesNacos, string? namingRoot,
            string? metadataRoot, string expectedNamingRoot, string expectedMetadataRoot)
        {
            var configuration = new DubboClient.DubboClientConf
            {
                DubboRootPath = namingRoot,
                MetadataRootPath = metadataRoot
            };

            Assert.Equal((expectedNamingRoot, expectedMetadataRoot),
                DubboClient.ResolveRegistryPaths(namingUsesNacos, metadataUsesNacos, configuration));
        }

        [Fact]
        public void ZooKeeperMetadataDefaultRoot_AgreesWithRegistryDefaultRoot()
        {
            // Metadata shares the ZooKeeper registry root even when Naming uses Nacos.
            Assert.Equal(ZooKeeperRegistryPaths.DefaultRoot, DubboMetadataDefaults.ZooKeeperRootPath);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("  ")]
        public void MetadataPath_DefaultRootRetainsMetadataAndProviderSegments(string? root)
        {
            Assert.Equal("/dubbo/metadata/com.foo.Service/provider/demo-app",
                DubboMetadataManager.BuildMetadataPath(root!, "com.foo.Service", null!, null!, "demo-app"));
        }

        [Fact]
        public void TypeDefinition_RecognizesCaseInsensitiveFieldsAndBothEnumSpellings()
        {
            const string json = """
                {
                  "canonicalName": "com.foo.Service",
                  "methods": [{ "name": "run", "parameterTypes": [], "returnType": "void" }],
                  "types": [{
                    "Id": "com.foo.User",
                    "TYPE": "com.foo.User",
                    "ITEMS": ["java.lang.String"],
                    "ENUM": ["ACTIVE"],
                    "enums": ["ACTIVE", "INACTIVE"],
                    "PROPERTIES": { "name": "java.lang.String" },
                    "$REF": "com.foo.User",
                    "TYPEBUILDERNAME": "JavaTypeBuilder"
                  }]
                }
                """;
            DubboMethodMetadata method = Assert.Single(DubboMetadataManager.ParseMetadataDocument(
                Encoding.UTF8.GetBytes(json), "com.foo.Service", new DubboServiceEndPointInfo(),
                "/dubbo/metadata/com.foo.Service/provider/demo-app", 1, DateTimeOffset.UnixEpoch));

            DubboTypeMetadata type = Assert.Single(method.TypeDefinitions);
            Assert.Equal("com.foo.User", type.Id);
            Assert.Equal("com.foo.User", type.Type);
            Assert.Equal("com.foo.User", type.Reference);
            Assert.Equal("JavaTypeBuilder", type.TypeBuilderName);
            Assert.Equal("java.lang.String", Assert.Single(type.Items).Type);
            Assert.Equal("java.lang.String", type.Properties["name"].Type);
            Assert.Equal(new[] { "ACTIVE", "INACTIVE" }, type.EnumValues);
        }
    }
}
