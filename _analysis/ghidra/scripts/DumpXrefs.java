// Dump references TO a list of addresses.
// Usage: DumpXrefs.java <outDir> <targetListFile>
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;
import java.io.BufferedReader;
import java.io.File;
import java.io.FileReader;
import java.io.FileWriter;
import java.util.ArrayList;
import java.util.List;

public class DumpXrefs extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 2) {
            println("usage: DumpXrefs.java <outDir> <targetListFile>");
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
        File out = new File(outDir, "xrefs.txt");
        FileWriter fw = new FileWriter(out);
        for (String raw : lines) {
            String addrText = raw;
            String comment = "";
            int hash = raw.indexOf('#');
            if (hash >= 0) {
                addrText = raw.substring(0, hash).trim();
                comment = raw.substring(hash + 1).trim();
            }
            Address target;
            try {
                target = toAddr(addrText);
            } catch (Exception e) {
                fw.write("BADADDR " + raw + "\n");
                continue;
            }
            fw.write("==== " + addrText + (comment.isEmpty() ? "" : "  # " + comment) + "\n");
            int n = 0;
            ReferenceIterator refs = currentProgram.getReferenceManager().getReferencesTo(target);
            while (refs.hasNext()) {
                Reference r = refs.next();
                Address from = r.getFromAddress();
                Function cf = getFunctionContaining(from);
                String fn = (cf == null) ? "(no function)" : (cf.getEntryPoint() + " " + cf.getName());
                fw.write("  from " + from + " [" + r.getReferenceType() + "] in " + fn + "\n");
                n++;
            }
            if (n == 0) {
                fw.write("  (no references)\n");
            }
        }
        fw.close();
        println("XREFS_OK file=" + out.getAbsolutePath());
    }
}
