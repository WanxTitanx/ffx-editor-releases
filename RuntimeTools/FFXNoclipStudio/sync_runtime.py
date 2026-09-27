#!/usr/bin/env python3
"""Reproduce the Studio addon in the pinned NoClip bundles, without external assets.
The source JS/CSS is maintained here; the logo comes from the editor's existing asset.
Upstream credits and license stay intact. --check detects drift without writing.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path

args = argparse.ArgumentParser()
args.add_argument('--check', action='store_true')
check = args.parse_args().check
source = Path(__file__).resolve().parent
root = source.parents[1]
logo = base64.b64encode((root/'FFXProjectEditor/Assets/spira-reforge-studio-256.png').read_bytes()).decode()
js = (source/'ffx-studio.js').read_text().replace('"/noclip/spira-reforge-studio-256.png"', '"data:image/png;base64,'+logo+'"')
css = (source/'ffx-studio.css').read_text()
start='<!-- ffx-studio-integration -->'
end='<!-- /ffx-studio-integration -->'
magic_js = (source/'magic-preview.js').read_text()
addon=f'{start}\n<style>{css}</style>\n<script type="module">{js}</script>\n<script type="module">{magic_js}</script>\n{end}'
for dist in ['dist', 'dist-ffxstudio']:
    path=root/'ExternalLibs/NoclipViewer'/dist/'index.html'
    text=path.read_text()
    if start in text:
        a=text.index(start); b=text.index(end,a)+len(end)
        updated=text[:a]+addon+text[b:]
    else:
        text=text.replace('<script type="module" src="/noclip/ffx-studio.js"></script>','')
        updated=text.replace('</head>',addon+'\n</head>',1)
    if check: assert updated == path.read_text(), f'Stale Studio addon: {path}'
    else: path.write_text(updated)
    path=root/'ExternalLibs/NoclipViewer'/dist/'static/js/461.18e06f8b.js'
    text=path.read_text()
    hooks={
        'editSave(){':'editSave(){if(window.ffxAuroraPositionEditor?.canSaveProject())return window.ffxAuroraPositionEditor.save();',
        'editReset(){':'editReset(){if(window.ffxAuroraPositionEditor?.canSaveProject())return window.ffxAuroraPositionEditor.resetSelection();',
        'editUpdate(e){':'editUpdate(e){window.ffxAuroraPositionEditor?.attach(this,e,eeI);',
        'r=e.camera.clipFromWorldMatrix,s=i.canvas.width,n=i.canvas.height,o=(1&t.buttons)!=0;':'r=e.camera.clipFromWorldMatrix,s=i.canvas.width,n=i.canvas.height,o=(1&t.buttons)!=0&&!window.ffxAuroraPositionEditor?.canSaveProject();',
        't.isKeyDownEventTriggered("KeyS")&&this.editSave()':'!window.ffxAuroraPositionEditor?.canSaveProject()&&t.isKeyDownEventTriggered("KeyS")&&this.editSave()',
        r' — clique outro = selecionar \xb7 S=salvar \xb7 R=reset':'${window.ffxAuroraPositionEditor?.canSaveProject()?"":" — S=salvar · R=reset"}',
    }
    updated=text.replace('window.ffxAuroraPositionEditor?.attach(this);', '')
    # Explicit PC-resource metadata bypasses only MIPS sniffing. The pinned NoClip
    # texture reader, PPP interpreter and renderer still consume the actual bytes.
    hooks.update({
        'this.currIndex=this.currShuffle.length,this.setMagicRandom()':
            'this.currIndex=this.currShuffle.length,window.ffxMagicPreview?.ownsSelection()||this.setMagicRandom()',
        's=await this.context.dataFetcher.fetchData(`FinalFantasyX/11/${(0,eD.yq)(i,4)}.bin`),n=ei8(i,s,null,r)':
            's=e.studio?new tI.A(await window.ffxMagicPreview.loadBinary(e.studio.binaryUrl)):await this.context.dataFetcher.fetchData(`FinalFantasyX/11/${(0,eD.yq)(i,4)}.bin`),n=ei8(i,s,null,r,e.studio)',
        'this.context.inputManager.isKeyDownEventTriggered("Space")&&this.setMagicRandom()':
            'this.context.inputManager.isKeyDownEventTriggered("Space")&&(window.ffxMagicPreview?.ownsSelection()?window.ffxMagicPreview.replay():this.setMagicRandom())',
        'function ei8(e,t,a,i){let r=function(e,t)':
            'function ei8(e,t,a,i,studio){let r=studio||function(e,t)',
        'this.allMagic=eiK.flat(),this.studioPanels=null,this.createPanels()':
            'this.allMagic=eiK.flat(),this.studioPanels=null,this.createPanels(),window.ffxMagicPreview?.attach(this,this.allMagic,eam.map(e=>e.rawOpcode))',
        'let e=super.createPanels(),t=new eS.Zk;t.customHeaderBackgroundColor=eS.pW,t.setTitle(eS.QU,"Magic Studio")':
            'let e=super.createPanels();if(window.ffxMagicPreview?.isManaged())return this.studioPanels=e;let t=new eS.Zk;t.customHeaderBackgroundColor=eS.pW,t.setTitle(eS.QU,"Magic Studio")',
    })
    hooks.update({
        'ea_(115,etb.order(2))': 'ea_(115,etb.order(2)),ea_(137,etb.order(6))',
        'default:console.warn("unimplemented euler order",a)': 'case 6:eh.hM(e,0),eh.Qr(e,e,t[2]),eh.eL(e,e,t[0]),eh.Z8(e,e,t[1]);break;default:console.warn("unimplemented euler order",a)',
        'n=ei8(i,s,null,r,e.studio);for(let e of': 'n=ei8(i,s,null,r,e.studio);e.studio&&await window.ffxMagicPreview.applyTextures(r,e.studio);for(let e of',
    })
    hooks.update({
        'if(n.magicProgram&&n.behaviors.length>1&&"vars"!==e.layout)': 'if(!e.studio&&n.magicProgram&&n.behaviors.length>1&&"vars"!==e.layout)',
    })
    for old,new in hooks.items():
        if new in updated: continue
        assert updated.count(old)==1, f'NoClip renderer hook drift: {old}'
        updated=updated.replace(old,new,1)
    if check: assert updated == text, f'Stale renderer hooks: {path}'
    else: path.write_text(updated)
manifest=root/'release/noclip-runtime.manifest.json'
data=json.loads(manifest.read_text())
for entry in data['studioBuild']['files']:
    if entry['path'] not in ['index.html','static/js/461.18e06f8b.js']: continue
    payload=(root/data['studioBuild']['outputRoot']/entry['path']).read_bytes()
    expected={'bytes':len(payload),'sha256':hashlib.sha256(payload).hexdigest()}
    if check:
        for key,value in expected.items(): assert entry[key]==value, f'Stale pin: {entry["path"]}'
    else: entry.update(expected)
if not check: manifest.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n')
print('Studio addon, renderer hooks and runtime pins are current.')
