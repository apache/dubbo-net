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

package com.example.dubbonet.provider;

import java.io.ByteArrayOutputStream;
import org.apache.dubbo.common.serialize.hessian2.Hessian2ObjectOutput;
import org.apache.dubbo.rpc.protocol.tri.TripleCustomerProtocolWrapper;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.assertEquals;

/** Independent Java producer for the fixed bytes in BinaryTripleCodecTest. */
public class TripleWrapperContractTest {
    @Test
    public void apacheWrapperAndHessianProduceTheCrossLanguageGoldenPayload() throws Exception {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        Hessian2ObjectOutput hessian = new Hessian2ObjectOutput(bytes);
        hessian.writeObject("ok");
        hessian.flushBuffer();
        byte[] wrapper = TripleCustomerProtocolWrapper.TripleResponseWrapper.Builder.newBuilder()
                .setSerializeType("hessian4")
                .setData(bytes.toByteArray())
                .setType("java.lang.String")
                .build().toByteArray();
        assertEquals("0a086865737369616e341203026f6b1a106a6176612e6c616e672e537472696e67", hex(wrapper));
    }

    private static String hex(byte[] bytes) {
        StringBuilder text = new StringBuilder();
        for (byte value : bytes) text.append(String.format("%02x", value & 255));
        return text.toString();
    }
}
