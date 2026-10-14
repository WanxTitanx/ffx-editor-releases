import test from 'node:test';
import assert from 'node:assert/strict';
import { battlePointToWorld, battleCameraMatrix, nativeBattleProjection, formationCamera, cameraFramesFormation, idleAnimation, loadIdleAnimation, createBattlePreview, readBattlePreviewRequest, selectBattlePreviewEncounter } from './battle-preview.js';

test('boss preview keeps the field namespace and full scene index', () => {
  assert.deepEqual(readBattlePreviewRequest('?battle=400&map=521&mapKind=field'),
    {encounter:400,map:521,kind:'field'});
  assert.deepEqual(readBattlePreviewRequest('?battle=261&map=37'),
    {encounter:261,map:37,kind:'battle'});
});
test('invalid preview coordinates cannot silently become arena zero', () => {
  for (const query of ['?battle=2&map=NaN','?battle=2&map=-1','?battle=2&map=521',
    '?battle=2&map=1024&mapKind=field','?battle=65536&map=1',
    '?battle=2&map=1oops','?battle=2&map=1&mapKind=unknown','?map=1'])
    assert.equal(readBattlePreviewRequest(query),null,query);
});
test('explicit preview uses its mounted scene and selected encounter without a random table', () => {
  const renderer={shatter:{enc:-1,map:25},shared:{}};
  assert.equal(selectBattlePreviewEncounter(renderer,{hash:'#ffx/battle-preview',
    search:'?battle=798&map=586&mapKind=field'}),true);
  assert.deepEqual(renderer.shatter,{enc:798,map:-1});
  assert.equal(selectBattlePreviewEncounter(renderer,{hash:'#ffx/016',
    search:'?battle=3&map=22'}),false);
  assert.deepEqual(renderer.shatter,{enc:798,map:-1});
});

const identity = [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
test('native projection covers continuous zoom values and both game aspect modes', () => {
  for(const aspect of [4/3,16/9])for(let depth=16;depth<=4096;depth+=.5){
    const p=nativeBattleProjection(depth,aspect);
    assert.ok(p&&Number.isFinite(p.fovY));
    assert.ok(Math.abs(1/(Math.tan(p.fovY/2)*aspect)-depth/320)<1e-10);
    // The projected horizontal coordinate of a point20units aside at z100 is
    // defined by the native focal length, independently of the display ratio.
    assert.ok(Math.abs((20/100)*p.scaleX-depth/1600)<1e-10);
  }
  for(const depth of [0,-1,NaN,Infinity])assert.equal(nativeBattleProjection(depth,16/9),null);
  for(const aspect of [0,-1,NaN,Infinity])assert.equal(nativeBattleProjection(600,aspect),null);
});
test('battle view uses the supplied coordinate transform, including the NoClip scale', () => {
  assert.deepEqual(battlePointToWorld([.1,0,0,0,0,-.1,0,0,0,0,-.1,0,0,0,0,1], [10,-20,30]), [1,2,-3]);
  assert.equal(battlePointToWorld(identity, [Infinity,0,0]), null);
});
test('camera looks at the target with an orthonormal frame and handles vertical views', () => {
  for (const eye of [[0,0,10], [0,10,0], [40,25,-100]]) {
    const matrix = battleCameraMatrix(eye, [0,0,0]);
    assert.ok(matrix.every(Number.isFinite));
    assert.deepEqual(matrix.slice(12,15), eye);
    const axes = [0,4,8].map(i => matrix.slice(i,i+3));
    for (const axis of axes) assert.ok(Math.abs(Math.hypot(...axis)-1)<1e-10);
    assert.ok(Math.abs(axes[0].reduce((n,x,i)=>n+x*axes[1][i],0))<1e-10);
  }
  assert.equal(battleCameraMatrix([0,0,0], [0,0,0]), null);
});
test('formation fallback stays finite for absent, coincident and incomplete formations', () => {
  assert.equal(formationCamera([], []), null);
  for (const positions of [[[0,0,0]], [[3,0,5],[3,0,5]], [[NaN,0,0],[30,-5,60]]]) {
    const view = formationCamera(positions, []);
    assert.ok([...view.eye,...view.target].every(Number.isFinite));
    assert.ok(view.eye[1] < view.target[1]);
  }
});
test('idle lookup prefers actual battle idle and rejects missing animation data', () => {
  const battle = Array(17).fill(-1); battle[16] = 0x10042;
  assert.deepEqual(idleAnimation({defaultAnimations:[[0x10000],battle]}), {group:1,index:16,id:0x10042,resource:1});
  assert.equal(idleAnimation({defaultAnimations:[[-1],[]]}), null);
});

test('character idle resolves from its common packet when battle packet is absent', async () => {
  const battle=Array(17).fill(-1); battle[16]=0x1109e;
  const resource={animations:[],fetched:7};
  const packets=[];
  const objects={ensureActorResource:()=>resource,async loadActorResource(id,packet){
    assert.equal(id,1); packets.push(packet);
    if(packet===0) resource.animations.push({id:0x1040});
  }};
  const idle=await loadIdleAnimation({defaultAnimations:[[0x11040],battle]},objects);
  assert.equal(idle.group,0); assert.equal(idle.id,0x11040);
  assert.deepEqual(packets,[1,0]);
});

test('camera rejects cropped formations, points behind it and non-finite anchors', () => {
  const matrix=battleCameraMatrix([0,0,10],[0,0,0]);
  assert.equal(cameraFramesFormation(matrix,identity,[[0,0,0],[2,1,0]]),true);
  for (const point of [[20,0,0],[0,0,20],[NaN,0,0]])
    assert.equal(cameraFramesFormation(matrix,identity,[point]),false);
});

function fixture() {
  let loads=0, rendered=0, destroyed=0, cameras=0;
  const idle=Array(17).fill(-1); idle[16]=0x10042;
  const scene={battleState:{monsters:[{pos:new Float32Array([0,0,40])}]},
    levelObjects:{cache:{device:{}},actorResources:new Map([[1,{model:{defaultAnimations:[[],idle]},animations:[],fetched:5}]]),
      ensureActorResource(id){return this.actorResources.get(id);},
      async loadActorResource(id,kind){assert.equal(id,1);assert.equal(kind,1);assert.equal(this.actorResources.get(id).fetched,1);this.actorResources.get(id).animations.push({id:0x42});}},
    async loadAndParseActorModel(id){assert.equal(id,1);loads++;}};
  const frame={camera:{worldMatrix:new Float32Array(identity),projectionMatrix:identity,worldMatrixUpdated(){cameras++;}}};
  const create=()=>({pos:new Float32Array(3),prevPos:new Float32Array(3),flags:0,
    animation:{running:false,start:0,end:30,setFromList(){this.running=true;}},render(){rendered++;},destroy(){destroyed++;}});
  const session={party:[0,1,2].map(slot=>({slot,x:slot*10,y:7,z:-20,label:`Tidus ${slot}`})),
    camera:{eye:[0,-20,-100],target:[0,0,0]}};
  return {scene,frame,create,session,counts:()=>({loads,rendered,destroyed,cameras})};
}
const settled = () => new Promise(resolve => setImmediate(resolve));
test('party loads once, renders three independent animated actors, and hiding preserves edits', async () => {
  const f=fixture(), preview=createBattlePreview();
  preview.configure(f.session); preview.prepare(f.scene,f.frame,identity,f.create);
  preview.showParty(true); await settled();
  assert.equal(preview.partyState(),'ready');
  assert.equal(preview.partyActors().length,3);
  assert.notEqual(preview.partyActors()[0].pos,preview.partyActors()[1].pos);
  preview.partyActors()[0].pos[0]=123;
  preview.render(f.scene,{},1,{},f.frame);
  preview.showParty(false); preview.render(f.scene,{},1,{},f.frame);
  preview.showParty(true); preview.prepare(f.scene,f.frame,identity,f.create);
  assert.equal(f.counts().loads,1); assert.equal(f.counts().rendered,3);
  assert.equal(preview.partyActors()[0].pos[0],123);
  assert.equal(preview.partyActors()[0].pos[1],7);
  preview.dispose(f.scene); assert.equal(f.counts().destroyed,3);
});
test('a party load finishing after disposal cannot publish actors into another scene', async () => {
  const f=fixture(), preview=createBattlePreview();
  let finish;
  f.scene.loadAndParseActorModel=()=>new Promise(resolve=>{finish=resolve;});
  preview.configure(f.session); preview.prepare(f.scene,f.frame,identity,f.create); preview.showParty(true);
  preview.dispose(f.scene); finish(); await settled();
  assert.deepEqual(preview.partyActors(),[]);
});
test('battle view restores the free camera when toggled off without changing actor positions', () => {
  const f=fixture(), preview=createBattlePreview();
  preview.configure(f.session); preview.prepare(f.scene,f.frame,identity,f.create);
  const before=Array.from(f.scene.battleState.monsters[0].pos);
  preview.setBattleView(true);
  assert.deepEqual(Array.from(f.frame.camera.worldMatrix).slice(12,15),[0,-20,-100]);
  preview.setBattleView(false);
  assert.deepEqual(Array.from(f.frame.camera.worldMatrix),identity);
  assert.deepEqual(Array.from(f.scene.battleState.monsters[0].pos),before);
});

test('native zoom changes projection and restores the exact free-view lens', () => {
  const f=fixture(), preview=createBattlePreview();
  const lens={fovY:.8,aspect:16/9,near:.5,far:1000,isOrthographic:false};
  Object.assign(f.frame.camera,lens,{setPerspective(fovY,aspect,near,far){
    Object.assign(this,{fovY,aspect,near,far,isOrthographic:false});
    this.projectionMatrix=[1/(Math.tan(fovY/2)*aspect),0,0,0,0,1/Math.tan(fovY/2),0,0,0,0,1,0,0,0,0,1];
  }});
  f.session.camera.screenDepth=600;
  preview.configure(f.session);preview.prepare(f.scene,f.frame,identity,f.create);preview.setBattleView(true);
  assert.ok(Math.abs(f.frame.camera.projectionMatrix[0]-1.875)<1e-10,'native horizontal focal coefficient: 600/320');
  assert.ok(Math.abs(f.frame.camera.projectionMatrix[5]-10/3)<1e-10,'HD vertical crop: 600/180');
  f.session.camera.screenDepth=1200;preview.prepare(f.scene,f.frame,identity,f.create);
  assert.ok(Math.abs(f.frame.camera.projectionMatrix[0]-3.75)<1e-10,'depth changes update zoom on the next frame');
  preview.setBattleView(false);
  for(const key of ['fovY','aspect','near','far','isOrthographic'])assert.equal(f.frame.camera[key],lens[key]);
});

test('scripted battle angle is retained even when the edited formation no longer fits its frame', () => {
  const f=fixture(), preview=createBattlePreview();
  f.session.camera={eye:[500,30,-10],target:[500,30,0],source:'atel'};
  preview.configure(f.session); preview.prepare(f.scene,f.frame,identity,f.create);
  preview.setBattleView(true);
  assert.equal(preview.hasFileCamera(),true);
  assert.deepEqual(Array.from(f.frame.camera.worldMatrix).slice(12,15),[500,30,-10]);
  preview.setBattleView(false);
  assert.deepEqual(Array.from(f.frame.camera.worldMatrix),identity);
});

test('a missing or unresolved ATEL pose keeps the explicit formation fallback', () => {
  const f=fixture(), preview=createBattlePreview();
  f.session.camera=null;
  preview.configure(f.session); preview.prepare(f.scene,f.frame,identity,f.create);
  preview.setBattleView(true);
  assert.equal(preview.hasFileCamera(),false);
  assert.ok(Array.from(f.frame.camera.worldMatrix).every(Number.isFinite));
});
