package com.runeliteoptimizer;

import net.runelite.client.RuneLite;
import net.runelite.client.externalplugins.ExternalPluginManager;
import net.runelite.client.plugins.Plugin;

/** Independent entry point: the two plugins enter the same normal startup cycle. */
public final class OptimizerLauncher {
    @SuppressWarnings("unchecked")
    static Class<? extends Plugin>[] plugins(Class<?> bridge) {
        if (!Plugin.class.isAssignableFrom(bridge) || !bridge.getName().equals("com.osrspath.bridge.OsrsPathBridgePlugin"))
            throw new IllegalArgumentException("Unsupported bridge class");
        return new Class[]{bridge, OptimizerTelemetryPlugin.class};
    }
    static void install() throws Exception {
        Class<?> bridge = Class.forName("com.osrspath.bridge.OsrsPathBridgePlugin");
        ExternalPluginManager.loadBuiltin(plugins(bridge));
    }
    public static void main(String[] args) throws Exception {
        install();
        if(args.length==1&&args[0].equals("--optimizer-registration-test")){System.out.println("PASS: packaged optimizer entry point registers both builtin plugins without starting the game.");return;}
        RuneLite.main(args);
    }
}
