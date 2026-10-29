import {readFile, writeFile, mkdir} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const input = path.join(root,'node_modules/three');
const output = path.join(root,'src/FootprintLinker/Web/vendor/three');
const files = {
  'build/three.module.js':'three.module.js', 'build/three.core.js':'three.core.js',
  'examples/jsm/controls/OrbitControls.js':'controls/OrbitControls.js',
  'examples/jsm/loaders/GLTFLoader.js':'loaders/GLTFLoader.js',
  'examples/jsm/utils/BufferGeometryUtils.js':'utils/BufferGeometryUtils.js',
  'examples/jsm/utils/SkeletonUtils.js':'utils/SkeletonUtils.js', 'LICENSE':'LICENSE.txt'
};
for(const [source,target] of Object.entries(files)) {
  const destination=path.join(output,target); await mkdir(path.dirname(destination),{recursive:true});
  const text=(await readFile(path.join(input,source),'utf8')).replaceAll("from 'three'", "from '/vendor/three/three.module.js'");
  await writeFile(destination,text);
}
console.log('Bundled local Three.js viewer assets');
