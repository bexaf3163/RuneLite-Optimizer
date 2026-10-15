package com.runeliteoptimizer;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.sun.net.httpserver.HttpServer;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.lang.reflect.Method;
import javax.inject.Inject;
import net.runelite.api.*;
import net.runelite.api.coords.WorldPoint;
import net.runelite.api.events.ClientTick;
import net.runelite.client.callback.ClientThread;
import net.runelite.client.config.Config;
import net.runelite.client.config.ConfigItem;
import net.runelite.client.config.ConfigManager;
import net.runelite.client.config.ProfileManager;
import net.runelite.client.eventbus.Subscribe;
import net.runelite.client.events.ProfileChanged;
import net.runelite.client.plugins.*;

@PluginDescriptor(name="RuneLite Optimizer Telemetry",description="Local benchmark observations and restricted graphics settings; no game inputs",enabledByDefault=true)
public final class OptimizerTelemetryPlugin extends Plugin {
    @Inject private Client client;
    @Inject private ClientThread clientThread;
    @Inject private PluginManager plugins;
    @Inject private ConfigManager configs;
    private final Gson gson=new GsonBuilder().serializeNulls().create();
    private final String session=UUID.randomUUID().toString();
    private volatile int revision;
    private volatile String snapshot="{}";
    private HttpServer server;
    private String token;
    private long lastSnapshot,cameraTravel,walkTravel;
    private Integer lastYaw;
    private WorldPoint previous;
    private static final Set<String> KEYS=new HashSet<>(Arrays.asList("drawDistance","expandedMapLoadingChunks","unlockFps","fpsTarget","vsyncMode","antiAliasingMode","anisotropicFilteringLevel","detailDistance","shadowMode","sceneResolutionScale","shadowResolution","shadowFiltering","dynamicLights","normalMapping","groundTextures","parallaxOcclusionMappingToggle","expandShadowDraw","terrainShadows","enableShadowTransparency"));
    private String generation(){return session+"-"+revision;}
    @Override protected void startUp() throws Exception {
        token=System.getProperty("runelite.optimizer.token","");
        if(!token.matches("[A-Fa-f0-9]{64}"))return;
        server=HttpServer.create(new InetSocketAddress("127.0.0.1",Integer.getInteger("runelite.optimizer.port",38283)),0);
        server.setExecutor(Executors.newSingleThreadExecutor(r->{Thread t=new Thread(r,"optimizer-telemetry");t.setDaemon(true);return t;}));
        server.createContext("/",ex->{
            int code=200;String body="{}";
            try {
                String supplied=ex.getRequestHeaders().getFirst("X-RuneLite-Optimizer");
                if(ex.getRequestHeaders().getFirst("Origin")!=null || supplied==null || !MessageDigest.isEqual(token.getBytes(StandardCharsets.US_ASCII),supplied.getBytes(StandardCharsets.US_ASCII))) {code=403;}
                else if(ex.getRequestURI().getPath().equals("/telemetry") && ex.getRequestMethod().equals("GET"))body=snapshot;
                else if(ex.getRequestURI().getPath().equals("/configure") && ex.getRequestMethod().equals("POST")){
                    byte[] input=ex.getRequestBody().readNBytes(16385);
                    if(input.length>16384)throw new IllegalArgumentException();
                    @SuppressWarnings("unchecked") Map<String,Object> request=gson.fromJson(new String(input,StandardCharsets.UTF_8),Map.class);
                    if(!generation().equals(request.get("generation")))throw new IllegalArgumentException();
                    @SuppressWarnings("unchecked") Map<String,Object> values=(Map<String,Object>)request.get("values");
                    @SuppressWarnings("unchecked") Map<String,Object> expected=(Map<String,Object>)request.get("expected");
                    if(values==null || expected==null || values.size()>20 || values.isEmpty())throw new IllegalArgumentException();
                    for(Map.Entry<String,Object> entry:values.entrySet())if(!valid(entry.getKey(),entry.getValue()) || !expected.containsKey(entry.getKey()))throw new IllegalArgumentException();
                    CountDownLatch finished=new CountDownLatch(1);AtomicBoolean cancel=new AtomicBoolean(),ok=new AtomicBoolean();String requestedGeneration=generation();
                    clientThread.invokeLater(()->{
                        Map<String,String> old=new LinkedHashMap<>();
                        try {
                            if(cancel.get() || !generation().equals(requestedGeneration))return;
                            for(String key:values.keySet()) {String[] parts=key.split("\\.",2);String current=configs.getConfiguration(parts[0],parts[1]);if(!Objects.equals(current,expected.get(key)))return;old.put(key,current);}
                            for(Map.Entry<String,Object> entry:values.entrySet())write(entry.getKey(),entry.getValue());
                            ok.set(true);lastSnapshot=0;
                        } catch(Exception error) {for(Map.Entry<String,String> entry:old.entrySet())try{write(entry.getKey(),entry.getValue());}catch(Exception ignored){}}
                        finally{finished.countDown();}
                    });
                    if(!finished.await(1500,TimeUnit.MILLISECONDS)){cancel.set(true);code=504;}else code=ok.get()?200:409;
                    body=ok.get()?"{\"ok\":true}":"{\"ok\":false}";
                } else code=405;
            } catch(Exception error){code=400;}
            byte[] bytes=body.getBytes(StandardCharsets.UTF_8);ex.getResponseHeaders().set("Content-Type","application/json; charset=utf-8");ex.getResponseHeaders().set("Cache-Control","no-store");ex.sendResponseHeaders(code,bytes.length);try(java.io.OutputStream out=ex.getResponseBody()){out.write(bytes);}finally{ex.close();}
        });
        server.start();
    }
    @Override protected void shutDown(){if(server!=null)server.stop(0);}
    @Subscribe public void onProfileChanged(ProfileChanged event){revision++;previous=null;lastYaw=null;}
    private void write(String key,Object value){String[] p=key.split("\\.",2);if(value==null)configs.unsetConfiguration(p[0],p[1]);else configs.setConfiguration(p[0],p[1],value.toString());}
    static boolean valid(String key,Object value){
        String[] p=key.split("\\.",2);if(p.length!=2 || !(p[0].equals("gpu")||p[0].equals("hd")) || !KEYS.contains(p[1]) || (p[0].equals("gpu") && Arrays.asList("detailDistance","shadowMode","sceneResolutionScale","shadowResolution","shadowFiltering","dynamicLights","normalMapping","groundTextures","parallaxOcclusionMappingToggle","expandShadowDraw","terrainShadows","enableShadowTransparency").contains(p[1])))return false;
        if(value==null)return true;if(!(value instanceof String))return false;String v=(String)value;
        switch(p[1]){
            case "normalMapping":case "groundTextures":case "parallaxOcclusionMappingToggle":case "expandShadowDraw":case "terrainShadows":case "enableShadowTransparency":case "unlockFps":return v.equals("true")||v.equals("false");
            case "vsyncMode":return Arrays.asList("OFF","ON","ADAPTIVE").contains(v);
            case "antiAliasingMode":return Arrays.asList("DISABLED","MSAA_2","MSAA_4","MSAA_8","MSAA_16").contains(v);
            case "shadowResolution":return Arrays.asList("RES_1024","RES_2048","RES_4096","RES_8192","RES_16384","RES_32768").contains(v);
            case "shadowFiltering":return Arrays.asList("NEAREST","SMOOTH_LOW","SMOOTH_HIGH","DITHERED_LOW","DITHERED_HIGH","PIXELATED","PCSS").contains(v);
            case "dynamicLights":return Arrays.asList("NONE","FEW","SOME","MANY").contains(v);
            case "shadowMode":return Arrays.asList("OFF","FAST","DETAILED").contains(v);
            default:try{int n=Integer.parseInt(v);if(!Integer.toString(n).equals(v))return false;int min=p[1].equals("detailDistance")?25:p[1].equals("sceneResolutionScale")?1:0;int max=p[1].equals("fpsTarget")?999:p[1].equals("expandedMapLoadingChunks")?5:p[1].equals("anisotropicFilteringLevel")?16:p[1].equals("sceneResolutionScale")?200:184;return n>=min&&n<=max;}catch(NumberFormatException error){return false;}
        }
    }
    @Subscribe public void onClientTick(ClientTick event){
        boolean logged=client.getGameState()==GameState.LOGGED_IN;
        WorldPoint point=logged&&client.getLocalPlayer()!=null?client.getLocalPlayer().getWorldLocation():null;
        int yaw=client.getCameraYaw();if(lastYaw!=null){int d=Math.abs(yaw-lastYaw)%2048;cameraTravel+=Math.min(d,2048-d);}lastYaw=yaw;
        if(point!=null && previous!=null && point.getPlane()==previous.getPlane()){int d=Math.abs(point.getX()-previous.getX())+Math.abs(point.getY()-previous.getY());if(d<16)walkTravel+=d;}previous=point;
        long now=System.currentTimeMillis();if(now-lastSnapshot<250)return;lastSnapshot=now;
        Map<String,Object> result=new LinkedHashMap<>();result.put("schema",2);result.put("pid",ProcessHandle.current().pid());result.put("generation",generation());result.put("timestamp",now);result.put("inGame",logged);result.put("world",client.getWorld());result.put("x",point==null?null:point.getX());result.put("y",point==null?null:point.getY());result.put("plane",point==null?null:point.getPlane());result.put("yaw",yaw);result.put("pitch",client.getCameraPitch());result.put("cameraTravel",cameraTravel);result.put("walkTravel",walkTravel);result.put("viewportWidth",client.getViewportWidth());result.put("viewportHeight",client.getViewportHeight());result.put("clientFps",client.getFPS());result.put("nearbyPlayers",client.getPlayers().size());
        Map<String,String> raw=new LinkedHashMap<>(),effective=new LinkedHashMap<>();String renderer="none";
        List<String> available=new ArrayList<>();available.add("none");
        for(Plugin plugin:plugins.getPlugins()){
            String name=plugin.getClass().getName();String group=name.equals("rs117.hd.HdPlugin")?"hd":name.equals("net.runelite.client.plugins.gpu.GpuPlugin")?"gpu":null;
            if(group==null)continue;available.add(group);
            if(plugins.isPluginActive(plugin)){if(!renderer.equals("none"))renderer="ambiguous";else renderer=group;}
            for(String key:KEYS)if(valid(group+"."+key,null))raw.put(group+"."+key,configs.getConfiguration(group,key));
            try{Config proxy=plugins.getPluginConfigProxy(plugin);if(proxy!=null)for(Class<?> type:proxy.getClass().getInterfaces())for(Method method:type.getMethods()){ConfigItem item=method.getAnnotation(ConfigItem.class);if(item!=null&&KEYS.contains(item.keyName())){Object value=method.invoke(proxy);effective.put(group+"."+item.keyName(),value instanceof Enum?((Enum<?>)value).name():String.valueOf(value));}}}catch(Exception ignored){}
        }
        raw.keySet().retainAll(effective.keySet());
        for(String key:Arrays.asList("runelite.gpuplugin","runelite.hdplugin","fpscontrol.limitFps")){String[] parts=key.split("\\.",2);String v=configs.getConfiguration(parts[0],parts[1]);raw.put(key,v);effective.put(key,v==null?"false":v);}
        if(renderer.equals("none")&&client.isGpu())renderer="unsupported";
        if((renderer.equals("gpu")||renderer.equals("hd"))&&!client.isGpu())renderer="initializing";
        result.put("availableRenderers",available);result.put("renderer",renderer);result.put("raw",raw);result.put("effective",effective);
        // Local authenticated transport only. Never include this private path in benchmark reports.
        if(configs.getProfile()!=null)result.put("configPath",ProfileManager.profileConfigFile(configs.getProfile()).getAbsolutePath());
        snapshot=gson.toJson(result);
    }
}
