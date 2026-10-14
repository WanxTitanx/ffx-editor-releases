import test from 'node:test';
import assert from 'node:assert/strict';
import { previewDescriptor, previewSessionPath, pppOpcodes, createPreviewPlayback } from './magic-preview.js';

const session = 'ab'.repeat(16), revision = 'cd'.repeat(32);
const snapshot = () => ({format:'ffx-pc-magic-v1', revision, magicId:721,
  label:'magic_0721.dll', binaryUrl:`/viewer-data/magic-preview/${session}/${revision}.bin`,
  headers:[13008], particleIndex:0, handlerNames:['pppSclMove','pppColor','newHandler'], usedHandlers:[0,1]});
test('unlisted and cloned DLL IDs load their working bytes without a catalogue substitute', () => {
  const desc = previewDescriptor(snapshot(), session, [], [6,11]);
  assert.equal(desc.main,721); assert.equal(desc.name,'magic_0721.dll');
  assert.deepEqual(desc.studio.funcMap,[6,11,65535]);
  assert.deepEqual(desc.studio.unsupported,[]);
});
test('only used unsupported handlers limit the preview', () => {
  const data=snapshot();data.usedHandlers.push(2);
  assert.deepEqual(previewDescriptor(data,session,[],[6,11]).studio.unsupported,['newHandler']);
});
test('revisions cannot fetch arbitrary files, traverse directories or use external URLs', () => {
  for (const binaryUrl of ['https://example.com/data.bin','/data/FinalFantasyX/11/0015.bin',`/viewer-data/magic-preview/${session}/../source.dll`]) {
    assert.throws(() => previewDescriptor({...snapshot(),binaryUrl},session,[],[]));
  }
  for (const id of ['../foo','',null]) assert.throws(() => previewSessionPath(id));
});
test('native catalogue setup is retained only for the exact effect', () => {
  const setup=()=>{};
  assert.equal(previewDescriptor(snapshot(),session,[{main:721,setup}],[]).studio.special,setup);
  assert.equal(previewDescriptor(snapshot(),session,[{main:3,setup}],[]).studio.special,undefined);
  assert.equal(pppOpcodes.pppKeShpTail2,26);
});
test('texture-only updates get a new revision while retaining the working DLL bytes', () => {
  const data=snapshot();
  data.workingRevision=revision; data.revision='ef'.repeat(32);
  data.textures=[{url:`/viewer-data/magic-preview/${session}/${'aa'.repeat(32)}.rgba.bin`,width:256,height:256,sourceWidth:128,sourceHeight:128}];
  assert.equal(previewDescriptor(data,session,[],[]).studio.binaryUrl,snapshot().binaryUrl);
  data.textures[0].url='https://example.com/texture.bin';
  assert.throws(()=>previewDescriptor(data,session,[],[]));
});

test('content-verified clone family preserves layout and auxiliary-resource policy, not source bytes', () => {
  const data = {...snapshot(), magicId:866, compatibilityMagicId:397, skipExtraFlipbooks:true};
  const setup=()=>{};
  const desc=previewDescriptor(data,session,[{main:397,layout:'parts',setup}],[]);
  assert.equal(desc.main,866);
  assert.equal(desc.layout,'parts');
  assert.equal(desc.studio.special,setup);
  assert.equal(desc.studio.skipExtraFlipbooks,true);
  assert.equal(desc.studio.binaryUrl,data.binaryUrl);
  for (const compatibilityMagicId of [-1,NaN,1.5,65536,'397'])
    assert.throws(()=>previewDescriptor({...data,compatibilityMagicId},session,[],[]));
  assert.throws(()=>previewDescriptor({...data,skipExtraFlipbooks:'yes'},session,[],[]));
});

test('pause, step and speed change only the preview clock', () => {
  const clock=createPreviewPlayback(), camera={id:'camera'}, input={deltaTime:20,time:1000,camera};
  assert.deepEqual(clock.advance(input),{deltaTime:20,time:20,camera});
  clock.setPaused(true);assert.equal(clock.advance(input).deltaTime,0);
  clock.step();const frame=clock.advance(input);
  assert.equal(frame.deltaTime,1000/30);assert.equal(clock.advance(input).time,frame.time);
  clock.setSpeed(.5);clock.setPaused(false);assert.equal(clock.advance(input).deltaTime,10);
  assert.equal(input.time,1000);assert.equal(input.deltaTime,20);
  clock.reset();assert.equal(clock.advance({deltaTime:NaN}).time,0);
  assert.equal(clock.advance({deltaTime:100000}).deltaTime,125);
  for(const speed of [-1,0,Infinity,NaN,3]) assert.throws(()=>clock.setSpeed(speed));
});
