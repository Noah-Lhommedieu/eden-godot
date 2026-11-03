# 📋 Next Session Plan - Phase 1.1.A → 1.1.B

**Current Status:** Phase 1.1.A: 60% (T_Class.ini ✅, S_Class.ini ⏳)  
**Blocker:** MyCrypt::crypt_decode decompilation needed  
**Target:** 100% Phase 1.1.A + Start Phase 1.1.B

---

## ⏱️ Session 2 Timeline (Estimated 2-3 hours)

### 🎯 Goal 1: Unlock S_Class.ini (60 min)

**Step 1: Ghidra Setup (10 min)**
```bash
1. Launch Ghidra
2. Open project: ghidra_projects/EdenZone.gpr
3. Load binary: server_us/ZoneServer/ZoneServer
4. Wait for auto-analysis (10-15 min)
5. Load symbols: docs/generated/ZoneServer-symbols.txt
```

**Step 2: Decompile MyCrypt::crypt_decode (30 min)**
```
1. Go to address: 0x08594f40
2. View decompiler panel
3. Analyze code structure
4. Document algorithm
5. Check for:
   - XOR operations
   - Substitution patterns
   - Key usage
   - Loop structures
```

**Step 3: Implement Decoder (20 min)**
```csharp
// ClassStatsParser.cs
private static byte[] DecodeWithMyCrypt(byte[] encoded, string key)
{
    // Based on Ghidra analysis
    byte[] decoded = new byte[encoded.Length];
    // [implementation here]
    return decoded;
}
```

### 🎯 Goal 2: Parse S_Class.ini (45 min)

**Step 1: Understand Format (15 min)**
- Decode sample data
- Identify structure
- Find class/level markers
- Identify stat fields

**Step 2: Implement Parser (20 min)**
```csharp
// ClassStatsParser.cs - Real implementation
public static Dictionary<(int, int), ClassStats> ParseClassStatsFile(...)
{
    var fileBytes = File.ReadAllBytes(filePath);
    var decoded = DecodeWithMyCrypt(fileBytes);
    // Parse decoded data into ClassStats
}
```

**Step 3: Test (10 min)**
- Parse 5 sample classes
- Verify data makes sense
- Check stats progression

### 🎯 Goal 3: Integrate with ClassDataService (45 min)

**Step 1: Add Stats Loading (15 min)**
```csharp
// ClassDataService.cs
public async Task LoadClassStatsAsync(string statsFilePath)
{
    var stats = await Task.Run(() => ClassStatsParser.ParseClassStatsFile(...));
    _statsCache = stats;
}
```

**Step 2: Add Stat Getters (15 min)**
```csharp
public ClassStats? GetClassStats(int classId, int level)
public IEnumerable<ClassStats> GetClassStatsByLevel(int classId)
```

**Step 3: Create Service Tests (15 min)**
```csharp
// Unit tests for stats loading and retrieval
```

### 🎯 Goal 4: Start Phase 1.1.B (30 min)

**Create ClassChangeValidator.cs**
```csharp
public class ClassChangeValidator
{
    public ValidationResult ValidateClassChange(Character player, int targetClassId)
    {
        // Check level requirement
        // Check class prerequisites
        // Check equipment compatibility
        // Check quests completed
        // Return ValidationResult
    }
}
```

---

## 📋 Checklist

### Pre-Session Prep
- [ ] Download latest Ghidra if needed
- [ ] Ensure ZoneServer binary accessible
- [ ] Backup current progress

### Ghidra Analysis
- [ ] Launch Ghidra
- [ ] Load ZoneServer
- [ ] Locate MyCrypt::crypt_decode @ 0x08594f40
- [ ] Export decompilation to docs/
- [ ] Document algorithm

### Implementation
- [ ] Implement MyCrypt decoder
- [ ] Test decoder on S_Class.ini
- [ ] Parse stats from decoded data
- [ ] Integrate with ClassDataService
- [ ] Write unit tests

### Validation
- [ ] Verify decoded data quality
- [ ] Check stat values are reasonable
- [ ] Compare with client expectations
- [ ] Run all tests

### Phase 1.1.B Start
- [ ] Create ClassChangeValidator
- [ ] Implement requirement checks
- [ ] Plan equipment switching logic

---

## 📁 Files to Modify

**Next Session Modifications:**
```
✅ Edit: Eden.Core/Data/Parsers/ClassStatsParser.cs
   - Implement DecodeWithMyCrypt
   - Implement ParseClassStatsFile
   
✅ Edit: Eden.Core/Game/Classes/ClassDataService.cs
   - Add stats caching
   - Add stat getters
   
✅ Create: Eden.Core/Game/Classes/ClassChangeValidator.cs
   - New file for validation logic
   
✅ Create: Eden.Tests/Game/Classes/ClassChangeValidatorTests.cs
   - Unit tests for validator
```

---

## 🔗 Related Documents

**Review Before Starting:**
- `docs/PHASE1-1A-COMPLETION-REPORT.md` - Current progress
- `docs/generated/GHIDRA-DECOMPILATION-PLAN.md` - Ghidra walkthrough
- `docs/generated/ZoneServer-symbols.txt` - Symbol reference

**Create During Session:**
- `docs/generated/GHIDRA-DECOMPILATION-RESULTS.md` - Analysis findings
- `docs/MYCRYPT-ALGORITHM.md` - Algorithm documentation

---

## ⚠️ Known Blockers

1. **Ghidra Decompilation:** Might be complex if algorithm uses custom operations
   - **Mitigation:** Save decompilation output, analyze offline
   
2. **Encoding:** S_Class.ini might use multiple encoding layers
   - **Mitigation:** Check for nested encoding in Ghidra output

3. **Binary Patching:** ZoneServer might be obfuscated
   - **Mitigation:** Use symbol table to locate exact functions

---

## 💡 Tips for Ghidra Success

1. **Use the Symbol Table:** Copy exact function addresses from ZoneServer-symbols.txt
2. **Export Decompilation:** Use Ghidra's export feature to save C code
3. **Track Data Types:** Note any struct definitions used by MyCrypt
4. **Look for Constants:** Encryption keys are often hardcoded
5. **Cross-reference:** See who calls crypt_decode for usage patterns

---

## 🎯 Success Criteria

**Session Complete When:**
- [x] MyCrypt::crypt_decode decompiled
- [x] Algorithm documented
- [x] Decoder implemented in C#
- [x] S_Class.ini successfully decoded
- [x] Stats parsed correctly
- [x] ClassDataService updated
- [x] All tests passing
- [x] Phase 1.1.B bootstrap started

---

## 📊 Expected Results

By end of next session:

```
Phase 1.1.A: 60% → 100% COMPLETE
├─ T_Class.ini: ✅ 100%
└─ S_Class.ini: ✅ 100%

Phase 1.1.B: 0% → 25% STARTED
├─ ClassChangeValidator: ✅ Created
└─ Tests prepared: ✅ Ready

Total Phase 1.1: 20% → 35% (35 of 100 points)
```

---

## 🚀 Quick Reference

**Critical Addresses:**
```
MyCrypt::crypt_decode:    0x08594f40  ← START HERE
MyCrypt::crypt_encode:    0x08595050
MyCrypt::str_crypt_decode: 0x08594e00
MyCrypt::str_crypt_encode: 0x08594e90
```

**File Paths:**
```
Binary:     server_us/ZoneServer/ZoneServer
T_Class:    server_us/Data/db/T_Class.ini
S_Class:    server_us/Data/db/S_Class.ini
Symbols:    docs/generated/ZoneServer-symbols.txt
Ghidra:     ghidra_projects/EdenZone.gpr
```

**Key Formulas:**
```
BaseID = (Awakening - 21) / 2 + 1
Branch1 = 20 + (BaseID - 1) * 2 + 1
Branch2 = Branch1 + 1
```

---

## 📞 Support

If stuck on Ghidra:
1. Check GHIDRA-DECOMPILATION-PLAN.md for detailed steps
2. Try exporting C code from decompiler view
3. Look for XOR or ROT operations in assembly
4. Check key scheduling in prologue

If decoder fails:
1. Verify file header: `<encode>` marker
2. Try XOR with different key values
3. Check for null termination
4. Compare decoded bytes against expected format

---

**Last Updated:** 2026-10-15  
**Status:** READY FOR NEXT SESSION  
**ETA:** Next session +2 hours Ghidra work = S_Class.ini DECODED
