#!/usr/bin/env python3
"""Build the offline adapter against an explicitly selected Hooks checkout."""
import argparse, hashlib, json, os, subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
HEADERS=['ElementPackAdmission.h','ElementPackCore.h','ElementPackJson.h','ElementRegistry.h','ElementAffinity.h',
         'ElementMonsterProof.h','MonsterRewardSettings.h','MonsterRewardsCore.h','SpiraAbilityCatalog.h','AutoAbilitySlots.h','VanguardCatalog.h']
def main():
    p=argparse.ArgumentParser();p.add_argument('--hooks-root',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args(); include=a.hooks_root.resolve()/'src/runtime/FfxHooksDll/hooks'; a.output=a.output.resolve(); a.output.mkdir(parents=True,exist_ok=True)
    workshop=a.hooks_root.resolve()/'research/equipment_workshop'
    extra=[include/'AeonAscensionCore.h',include/'ArcanaStore.h',include/'ArcanaCore.h',include/'ArcanaCatalog.generated.h',
           workshop/'include/workshop.h',workshop/'include/effects.h',workshop/'include/lifecycle.h',
           workshop/'include/customize_recipes.h',workshop/'include/customize_constraints.h']
    sources=[workshop/'src/workshop.cpp',workshop/'src/lifecycle.cpp',workshop/'src/effects.cpp',include/'ArcanaCore.cpp',include/'ArcanaStore.cpp']
    files=[include/h for h in HEADERS]+extra+sources+[ROOT/'tools/hook-contracts/hook_contracts.cpp']
    hashes={str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in files}
    # Pin dependency hashes on first build; a changed contract must be reviewed explicitly.
    lock=ROOT/'tools/hook-contracts/sources.json'
    relative={str(f.relative_to(a.hooks_root.resolve())):hashes[str(f)] for f in files if f != files[-1]}
    if lock.exists() and json.loads(lock.read_text())!=relative:raise SystemExit('Hook consumer sources changed; review sources.json before rebuilding')
    if not lock.exists():lock.write_text(json.dumps(relative,indent=2)+'\n')
    binary=a.output/('hook-contracts.exe' if os.name=='nt' else 'hook-contracts')
    if os.name=='nt':cmd=['cl','/nologo','/std:c++17','/EHsc','/I'+str(include),'/I'+str(workshop/'include'),str(files[-1]),*[str(p) for p in sources],'/Fe:'+str(binary),'bcrypt.lib']
    else:cmd=['g++','-std=c++17','-O2','-Wall','-Wextra','-I'+str(include),'-I'+str(workshop/'include'),str(files[-1]),*[str(p) for p in sources],'-lcrypto','-o',str(binary)]
    subprocess.run(cmd,check=True,cwd=a.output)
    subprocess.run([str(binary.resolve()),'identity'],check=True)
    (a.output/'build-inventory.json').write_text(json.dumps({'sources':hashes,'binary_sha256':hashlib.sha256(binary.read_bytes()).hexdigest(),'command':cmd},indent=2)+'\n')
if __name__=='__main__':main()
