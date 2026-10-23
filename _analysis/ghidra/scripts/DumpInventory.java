// Dump a function inventory (CSV) for the current program.
// Usage: DumpInventory.java <outDir>
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import java.io.File;
import java.io.FileWriter;

public class DumpInventory extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 1) {
            println("usage: DumpInventory.java <outDir>");
            return;
        }
        File outDir = new File(args[0]);
        outDir.mkdirs();
        File out = new File(outDir, "functions.csv");
        FileWriter fw = new FileWriter(out);
        fw.write("entry,name,size,isThunk,paramCount\n");
        int n = 0;
        FunctionIterator it = currentProgram.getFunctionManager().getFunctions(true);
        while (it.hasNext()) {
            Function f = it.next();
            String entry = f.getEntryPoint().toString();
            String name = f.getName();
            long size = f.getBody().getNumAddresses();
            boolean thunk = f.isThunk();
            int pc = f.getParameterCount();
            fw.write(entry + "," + name + "," + size + "," + thunk + "," + pc + "\n");
            n++;
        }
        fw.close();
        println("INVENTORY_OK count=" + n + " file=" + out.getAbsolutePath());
    }
}
