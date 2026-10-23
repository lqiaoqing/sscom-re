// Decompile a list of addresses (one per line, "0x..." or hex, optional trailing comment after #).
// For each address, decompile the containing function and write
//   <outDir>/<entry>_<name>.c
// Usage: DumpDecomp.java <outDir> <addrListFile>
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import java.io.BufferedReader;
import java.io.File;
import java.io.FileReader;
import java.io.FileWriter;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public class DumpDecomp extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 2) {
            println("usage: DumpDecomp.java <outDir> <addrListFile>");
            return;
        }
        File outDir = new File(args[0]);
        outDir.mkdirs();
        List<String> lines = new ArrayList<>();
        BufferedReader br = new BufferedReader(new FileReader(args[1]));
        String line;
        while ((line = br.readLine()) != null) {
            line = line.trim();
            if (!line.isEmpty() && !line.startsWith("#")) {
                lines.add(line);
            }
        }
        br.close();
        DecompInterface ifc = new DecompInterface();
        ifc.setOptions(new ghidra.app.decompiler.DecompileOptions());
        if (!ifc.openProgram(currentProgram)) {
            println("DECOMP_OPEN_FAILED");
            return;
        }
        Map<String, String> done = new LinkedHashMap<>();
        int ok = 0, fail = 0;
        for (String raw : lines) {
            String addrText = raw;
            String comment = "";
            int hash = raw.indexOf('#');
            if (hash >= 0) {
                addrText = raw.substring(0, hash).trim();
                comment = raw.substring(hash + 1).trim();
            }
            Address addr;
            try {
                addr = toAddr(addrText);
            } catch (Exception e) {
                println("BADADDR " + raw);
                fail++;
                continue;
            }
            Function f = getFunctionAt(addr);
            if (f == null) {
                f = getFunctionContaining(addr);
            }
            if (f == null) {
                println("NOFUNC " + addrText + " " + comment);
                fail++;
                continue;
            }
            String key = f.getEntryPoint().toString();
            String fileName = String.format("%s_%s.c", key, f.getName());
            if (done.containsKey(key)) {
                println("DUP " + addrText + " -> " + key + " (already dumped)");
                continue;
            }
            DecompileResults res = ifc.decompileFunction(f, 120, monitor);
            if (res == null || res.getDecompiledFunction() == null) {
                println("DECOMP_FAIL " + addrText + " " + f.getName());
                fail++;
                continue;
            }
            String code = res.getDecompiledFunction().getC();
            FileWriter fw = new FileWriter(new File(outDir, fileName));
            fw.write("// request: " + addrText + (comment.isEmpty() ? "" : "  # " + comment) + "\n");
            fw.write("// entry: " + key + "  name: " + f.getName()
                    + "  size: " + f.getBody().getNumAddresses() + "\n\n");
            fw.write(code);
            fw.close();
            done.put(key, fileName);
            println("OK " + addrText + " -> " + key + " " + f.getName());
            ok++;
        }
        ifc.dispose();
        println("DECOMP_DONE ok=" + ok + " fail=" + fail);
    }
}
