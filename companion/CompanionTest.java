package com.runeliteoptimizer;
import com.sun.net.httpserver.HttpServer;
import java.net.*;
import java.lang.reflect.Field;
import java.nio.charset.StandardCharsets;

public final class CompanionTest {
    static int request(int port,String path,String method,String token,String origin,String body)throws Exception {
        HttpURLConnection c=(HttpURLConnection)new URL("http://127.0.0.1:"+port+path).openConnection();c.setRequestMethod(method);c.setReadTimeout(2000);c.setConnectTimeout(2000);
        if(token!=null)c.setRequestProperty("X-RuneLite-Optimizer",token);if(origin!=null)c.setRequestProperty("Origin",origin);
        if(body!=null){c.setDoOutput(true);c.getOutputStream().write(body.getBytes(StandardCharsets.UTF_8));}
        int code=c.getResponseCode();c.disconnect();return code;
    }
    static void check(boolean ok,String label){if(!ok)throw new AssertionError(label);}
    public static void main(String[] args)throws Exception {
        OptimizerLauncher.install();Class<?>[] registered=OptimizerLauncher.plugins(Class.forName("com.osrspath.bridge.OsrsPathBridgePlugin"));
        check(registered.length==2&&registered[0].getName().equals("com.osrspath.bridge.OsrsPathBridgePlugin")&&registered[1]==OptimizerTelemetryPlugin.class,"Original bridge and optimizer registered together");
        check(OptimizerTelemetryPlugin.valid("hd.fpsTarget","999"),"FPS profile");check(OptimizerTelemetryPlugin.valid("hd.shadowMode",null),"Unset for exact restoration");
        check(!OptimizerTelemetryPlugin.valid("runelite.gpuplugin","true"),"No plugin activation");check(!OptimizerTelemetryPlugin.valid("hd.fpsTarget","1000"),"FPS bound");check(!OptimizerTelemetryPlugin.valid("gpu.detailDistance","25"),"Renderer-specific keys");check(!OptimizerTelemetryPlugin.valid("hd.shadowMode","FAKE"),"Enum");
        System.setProperty("sun.net.http.allowRestrictedHeaders","true");String token="a".repeat(64);System.setProperty("runelite.optimizer.token",token);System.setProperty("runelite.optimizer.port","0");
        OptimizerTelemetryPlugin plugin=new OptimizerTelemetryPlugin();plugin.startUp();Field f=OptimizerTelemetryPlugin.class.getDeclaredField("server");f.setAccessible(true);int port=((HttpServer)f.get(plugin)).getAddress().getPort();
        try {check(request(port,"/telemetry","GET",null,null,null)==403,"Auth required");check(request(port,"/telemetry","GET",token,"http://example.com",null)==403,"Browser Origin rejected");check(request(port,"/telemetry","GET",token,null,null)==200,"Authenticated read");check(request(port,"/teleport","POST",token,null,"{}")==405,"No gameplay endpoint");check(request(port,"/configure","POST",token,null,"{}")==400,"Missing generation rejected");}
        finally{plugin.shutDown();}
        System.out.println("PASS: isolated companion validation, token/Origin gates and no gameplay endpoint.");
        if(args.length==1&&args[0].equals("--agent-smoke"))System.exit(0);
    }
}
