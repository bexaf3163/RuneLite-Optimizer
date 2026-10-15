package com.runeliteoptimizer;

import net.runelite.client.RuneLite;
import net.runelite.client.plugins.Plugin;
import net.runelite.client.plugins.PluginManager;
import javax.swing.SwingUtilities;
import java.util.Collections;
import java.util.concurrent.atomic.AtomicBoolean;

/** Registers an independent plugin; does not transform or edit client/OSRS Path classes. */
public final class OptimizerAgent {
    public static void premain(String ignored) {
        AtomicBoolean finished = new AtomicBoolean();
        Thread loader = new Thread(() -> {
            long deadline = System.nanoTime() + 120_000_000_000L;
            while (!finished.get() && System.nanoTime() < deadline) {
                try {
                    SwingUtilities.invokeAndWait(() -> {
                        if (finished.get() || RuneLite.getInjector() == null) return;
                        PluginManager manager = RuneLite.getInjector().getInstance(PluginManager.class);
                        // Wait for the original launcher's plugin to finish loading. Never replace
                        // ExternalPluginManager's builtin list: loadBuiltin overwrites that list.
                        boolean ready = manager.getPlugins().stream().anyMatch(p ->
                            p.getClass().getName().equals("com.osrspath.bridge.OsrsPathBridgePlugin") && manager.isPluginActive(p));
                        if (!ready) return;
                        finished.set(true);
                        try {
                            java.util.List<Plugin> plugins = manager.loadPlugins(Collections.singletonList(OptimizerTelemetryPlugin.class), null);
                            manager.loadDefaultPluginConfiguration(plugins);
                            for (Plugin plugin : plugins) manager.startPlugin(plugin);
                        } catch (Throwable error) {
                            System.err.println("RuneLite Optimizer telemetry could not load; normal launcher continues.");
                        }
                    });
                    Thread.sleep(250);
                } catch (Throwable error) { finished.set(true); }
            }
        }, "runelite-optimizer-loader");
        loader.setDaemon(true);
        loader.start();
    }
}
