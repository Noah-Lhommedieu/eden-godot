import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.program.model.listing.Function;
import ghidra.program.model.address.Address;

public class DecompileFunction extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        String target = args.length > 0 ? args[0] : "0x08057710";
        Address addr = null;
        try {
            addr = toAddr(target);
        } catch (Exception e) {
            // Not an address, maybe search by name
        }
        Function func = null;
        if (addr != null) {
            func = getFunctionAt(addr);
            if (func == null) {
                func = getFunctionContaining(addr);
            }
        }
        if (func == null) {
            for (Function f : currentProgram.getFunctionManager().getFunctions(true)) {
                if (f.getName().contains(target)) {
                    func = f;
                    break;
                }
            }
        }
        if (func == null) {
            println("ERROR: Function not found: " + target);
            return;
        }
        println("=== DECOMPILED: " + func.getName() + " @ " + func.getEntryPoint() + " ===");
        DecompInterface decomp = new DecompInterface();
        decomp.openProgram(currentProgram);
        DecompileResults res = decomp.decompileFunction(func, 60, monitor);
        if (res.decompileCompleted()) {
            println(res.getDecompiledFunction().getC());
        } else {
            println("ERROR decompile: " + res.getErrorMessage());
        }
    }
}
