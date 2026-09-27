import test from 'node:test';
import assert from 'node:assert/strict';
import { previewDescriptor, previewSessionPath, pppOpcodes } from './magic-preview.js';

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
