import test from 'node:test';
import assert from 'node:assert/strict';
import { collectPositions, bridgeAddress, hasPositionChanges, applyCoordinate, invertMatrix4, createPlaneDrag } from './ffx-studio.js';
test('saves all X/Z coordinates without sending visual flight height', () => {
 assert.deepEqual(collectPositions([{pos:[12,999,34]},{pos:[-4,0,18]}]),[{slot:0,x:12,z:34},{slot:1,x:-4,z:18}]);
});
test('rejects non-finite, unbounded and empty formations', () => {
 for(const input of [[],[{pos:[NaN,0,2]}],[{pos:[1,0,Infinity]}],[{pos:[1000001,0,2]}]]) assert.throws(()=>collectPositions(input));
});
test('bridge is restricted to local authenticated routes', () => {
 assert.equal(bridgeAddress('http://127.0.0.1:1234','/positions'),'http://127.0.0.1:1234/api/aurora/positions');
 for(const args of [['https://example.com','/positions'],['http://127.0.0.1','/other']]) assert.throws(()=>bridgeAddress(...args));
});
test('float32 JSON round-trip does not mark untouched positions as edited', () => {
 assert.equal(hasPositionChanges([{pos:new Float32Array([2.0611458,0,35.06904])}],[{x:2.0611458,z:35.06904}]),false);
});
test('editing X preserves the full precision of Z and visual Y', () => {
 const monster={pos:new Float32Array([2.0611458,123,35.06904])}; const z=monster.pos[2];
 assert.equal(applyCoordinate(monster,'X','25'),true);
 assert.equal(monster.pos[0],25); assert.equal(monster.pos[1],123); assert.equal(monster.pos[2],z);
 assert.equal(applyCoordinate(monster,'Y','0'),false);
});
const identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
const topCamera=[1,0,0,0,0,0,-1,0,0,-1,0,0,0,0,0,1];
test('plane drag follows mouse deltas without snapping the selected actor to the pointer',()=>{
 const drag=createPlaneDrag(topCamera,identity,[10,3,20],{left:0,top:0,width:200,height:200},{x:40,y:80});
 assert.deepEqual(drag({x:40,y:80}),{x:10,z:20});
 assert.deepEqual(drag({x:90,y:130}),{x:10.5,z:20.5});
});
test('plane drag honours the renderer transform and CSS canvas rectangle',()=>{
 const transform=[2,0,0,0,0,2,0,0,0,0,2,0,100,50,60,1];
 const drag=createPlaneDrag(topCamera,transform,[10,3,20],{left:100,top:200,width:400,height:400},{x:200,y:300});
 const result=drag({x:300,y:400});
 assert.ok(Math.abs(result.x-10.25)<1e-10); assert.ok(Math.abs(result.z-20.25)<1e-10);
});
test('singular matrices and a camera ray parallel to the movement plane fail closed',()=>{
 assert.equal(invertMatrix4(new Array(16).fill(0)),null);
 assert.equal(createPlaneDrag(identity,identity,[0,0,0],{left:0,top:0,width:200,height:200},{x:100,y:100}),null);
});
test('the infinite far plane used by NoClip still produces a finite drag ray',()=>{
 const infinitePerspective=[1,0,0,0,0,1,0,0,0,0,-1,-1,0,-10,-0.2,0];
 const drag=createPlaneDrag(infinitePerspective,identity,[3,0,-10],{left:0,top:0,width:200,height:200},{x:100,y:150});
 assert.ok(drag);
 const result=drag({x:150,y:150});
 assert.ok(Math.abs(result.x-13)<1e-10); assert.ok(Math.abs(result.z+10)<1e-10);
});
