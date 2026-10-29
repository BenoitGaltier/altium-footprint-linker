import * as THREE from '/vendor/three/three.module.js';
import {OrbitControls} from '/vendor/three/controls/OrbitControls.js';
import {GLTFLoader} from '/vendor/three/loaders/GLTFLoader.js';

const $ = id => document.getElementById(id);
const warnings = {
  previewNoBody:'Aucun corps 3D n’est disponible dans ce fichier. La vue 3D montre les primitives de l’empreinte.',
  previewMissingModels:'Certains modèles 3D liés sont absents ou n’ont pas pu être convertis. Les fichiers externes ne sont pas chargés automatiquement.',
  preview3dFailed:'Le modèle 3D n’a pas pu être converti. La vue 2D reste disponible.',
  previewTooLarge3d:'Le modèle 3D dépasse la limite d’aperçu. La vue 2D reste disponible.'
};
function disposeObject(object) {
  object.traverse(item => { item.geometry?.dispose(); const materials=Array.isArray(item.material)?item.material:[item.material]; for(const m of materials) m?.dispose(); });
}
export function createPreview(api) {
  let generation=0, data=null, renderer=null, scene=null, camera=null, orbit=null, observer=null, frame=0, imageUrl=null;
  let zoom=1, panX=0, panY=0, drag=null, mode='2d';
  const image=$('previewImage'), stage=$('previewStage'), dialog=$('previewDialog');
  function clear() {
    cancelAnimationFrame(frame); observer?.disconnect(); observer=null; orbit?.dispose(); orbit=null;
    if(scene) disposeObject(scene); scene=null;
    renderer?.dispose(); renderer?.forceContextLoss(); renderer=null; $('preview3d').replaceChildren();
    if(imageUrl) URL.revokeObjectURL(imageUrl); imageUrl=null; image.removeAttribute('src');
  }
  function close() { generation++; clear(); data=null; if(dialog.open) dialog.close(); }
  function transform() { image.style.transform=`translate(${panX}px,${panY}px) scale(${zoom})`; }
  function fit3d() {
    if(!camera||!scene||!orbit)return;
    const object=scene.getObjectByName('Footprint'); if(!object)return;
    const box=new THREE.Box3().setFromObject(object), center=box.getCenter(new THREE.Vector3()), size=box.getSize(new THREE.Vector3());
    const radius=Math.max(size.length()/2,0.0001), distance=radius/Math.tan(camera.fov*Math.PI/360)*1.25;
    orbit.target.copy(center); camera.up.set(0,1,0); camera.position.copy(center).addScaledVector(new THREE.Vector3(1,.8,1).normalize(),distance);
    camera.near=radius/1000; camera.far=radius*1000; camera.updateProjectionMatrix(); orbit.update();
  }
  function reset() { zoom=1; panX=0; panY=0; transform(); fit3d(); }
  function selectMode(value) {
    mode=value; $('preview2d').hidden=value!=='2d'; $('preview3d').hidden=value!=='3d';
    $('previewMode2d').setAttribute('aria-selected',String(value==='2d'));
    $('previewMode3d').setAttribute('aria-selected',String(value==='3d'));
    $('previewHelp').textContent=value==='2d'?'Molette : zoom · Glisser : déplacer · SVG vectoriel':'Glisser : tourner · Clic droit : déplacer · Molette : zoom';
    $('previewDownload').textContent=value==='3d'?'Exporter GLB':'Exporter SVG';
    $('previewDownload').disabled=!data||(value==='3d'&&(!data.glb||data.extrusions.length>0));
    if(value==='3d'&&renderer) { resize(); if(!frame) animate(); }
  }
  function resize() {
    if(!renderer||!camera)return;
    const box=$('preview3d').getBoundingClientRect();
    renderer.setSize(Math.max(box.width,100),Math.max(box.height,100),false); camera.aspect=Math.max(box.width,100)/Math.max(box.height,100); camera.updateProjectionMatrix();
  }
  function animate() { frame=requestAnimationFrame(animate); if(mode==='3d'&&renderer&&scene&&camera) { orbit.update(); renderer.render(scene,camera); } }
  async function build3d(value, token) {
    const content=new THREE.Group(); content.name='Footprint';
    if(value.glb) {
      const bytes=Uint8Array.from(atob(value.glb),c=>c.charCodeAt(0));
      const gltf=await new GLTFLoader().parseAsync(bytes.buffer,''); content.add(gltf.scene);
    }
    if(token!==generation) { disposeObject(content); return; }
    for(const body of value.extrusions) {
      const shape=new THREE.Shape(); body.outline.forEach((p,i)=>i?shape.lineTo(p[0]-value.centerX,p[1]-value.centerY):shape.moveTo(p[0]-value.centerX,p[1]-value.centerY)); shape.closePath();
      const geometry=new THREE.ExtrudeGeometry(shape,{depth:body.height,bevelEnabled:false});
      geometry.translate(0,0,body.base); geometry.rotateX(-Math.PI/2); geometry.scale(.001,.001,.001);
      content.add(new THREE.Mesh(geometry,new THREE.MeshStandardMaterial({color:body.color,roughness:.65,metalness:.05,opacity:body.opacity,transparent:body.opacity<1,side:THREE.DoubleSide})));
    }
    renderer=new THREE.WebGLRenderer({antialias:true,alpha:false}); renderer.setPixelRatio(Math.min(devicePixelRatio,2)); renderer.setClearColor('#e9eeeb');
    renderer.outputColorSpace=THREE.SRGBColorSpace; renderer.toneMapping=THREE.ACESFilmicToneMapping;
    $('preview3d').append(renderer.domElement); scene=new THREE.Scene(); scene.add(content);
    scene.add(new THREE.HemisphereLight(0xffffff,0x65786c,2.8));
    for(const [x,y,z] of [[2,3,4],[-3,1,-2]]) { const light=new THREE.DirectionalLight(0xffffff,2.2); light.position.set(x,y,z); scene.add(light); }
    camera=new THREE.PerspectiveCamera(38,1,.000001,100); orbit=new OrbitControls(camera,renderer.domElement); orbit.enableDamping=true;
    observer=new ResizeObserver(resize); observer.observe($('preview3d')); fit3d(); resize(); animate();
  }
  async function open(planId,rowId) {
    generation++; clear(); const token=generation; data=null; reset(); selectMode('2d');
    $('previewTitle').textContent='Aperçu de l’empreinte'; $('previewMeta').textContent='Lecture locale du .PcbLib…';
    $('previewNotice').hidden=true; $('previewMode3d').disabled=true; $('previewDownload').disabled=true;
    if(!dialog.open)dialog.showModal();
    try {
      const result=await api('preview','POST',{planId,rowId}); if(token!==generation)return; data=result;
      $('previewTitle').textContent=result.name; $('previewMeta').textContent=`${result.file} · ${result.pads} pastille(s) · ${result.widthMm.toFixed(2)} × ${result.heightMm.toFixed(2)} mm · ${result.renderedModels} modèle(s) STEP affiché(s)`;
      imageUrl=URL.createObjectURL(new Blob([result.svg],{type:'image/svg+xml'})); image.src=imageUrl;
      $('previewNotice').textContent=result.warnings.map(w=>warnings[w]||w).join('\n'); $('previewNotice').hidden=!result.warnings.length;
      $('previewDownload').disabled=false;
      if(result.glb||result.extrusions.length) {
        try { await build3d(result,token); if(token===generation)$('previewMode3d').disabled=false; }
        catch { if(token===generation){$('previewNotice').textContent+='\nLa vue 3D nécessite WebGL 2 dans le navigateur.';$('previewNotice').hidden=false;} }
      }
    } catch(error) { if(token===generation) { $('previewNotice').textContent=error.message; $('previewNotice').hidden=false; $('previewMeta').textContent='Aperçu indisponible'; } }
  }
  $('previewClose').addEventListener('click',close); dialog.addEventListener('cancel',event=>{event.preventDefault();close();});
  $('previewMode2d').addEventListener('click',()=>selectMode('2d')); $('previewMode3d').addEventListener('click',()=>selectMode('3d')); $('previewReset').addEventListener('click',reset);
  stage.addEventListener('wheel',event=>{if(mode!=='2d')return; event.preventDefault(); zoom=Math.min(20,Math.max(.15,zoom*Math.exp(-event.deltaY*.0015)));transform();},{passive:false});
  stage.addEventListener('pointerdown',event=>{if(mode!=='2d')return;drag={x:event.clientX-panX,y:event.clientY-panY};stage.setPointerCapture(event.pointerId);});
  stage.addEventListener('pointermove',event=>{if(!drag||mode!=='2d')return;panX=event.clientX-drag.x;panY=event.clientY-drag.y;transform();});
  stage.addEventListener('pointerup',()=>{drag=null;}); stage.addEventListener('pointercancel',()=>{drag=null;});
  $('previewDownload').addEventListener('click',()=>{
    if(!data)return; const glb=mode==='3d'&&data.glb;
    const bytes=glb?Uint8Array.from(atob(data.glb),c=>c.charCodeAt(0)):data.svg;
    const url=URL.createObjectURL(new Blob([bytes],{type:glb?'model/gltf-binary':'image/svg+xml'})); const a=document.createElement('a');a.href=url;a.download=data.name.replace(/[\\/:*?"<>|]/g,'_')+(glb?'.glb':'.svg');a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
  });
  return {open,close};
}
