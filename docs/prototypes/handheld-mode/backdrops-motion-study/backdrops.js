// EmuShelf console backdrop prototypes: every renderer, for all sixteen consoles.
// Loaded by console-rotation.html (random rotation viewer) and rate.html (review page).
// Prototype code only: CPU software rendering into small canvases, not production renderers.
// SNES/PS1 renderers are Codex's, copied unchanged from experiments.fragment.html.
(()=>{
'use strict';
// ---------- shared math ----------
const clamp=(v,a,b)=>v<a?a:v>b?b:v, lerp=(a,b,t)=>a+(b-a)*t, mod=(v,n)=>(v%n+n)%n;
const smooth=(a,b,x)=>{const t=clamp((x-a)/(b-a),0,1);return t*t*(3-2*t);};
const add=(a,b)=>[a[0]+b[0],a[1]+b[1],a[2]+b[2]], sub=(a,b)=>[a[0]-b[0],a[1]-b[1],a[2]-b[2]], mul=(a,k)=>[a[0]*k,a[1]*k,a[2]*k];
const dot=(a,b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2], norm=a=>{const l=Math.hypot(a[0],a[1],a[2])||1;return [a[0]/l,a[1]/l,a[2]/l];};
const mix=(a,b,t)=>[lerp(a[0],b[0],t),lerp(a[1],b[1],t),lerp(a[2],b[2],t)];
let seed=7;const rnd=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/4294967296;};
function rotator(rx,ry,rz){const a=Math.cos(rx),b=Math.sin(rx),c=Math.cos(ry),d=Math.sin(ry),e=Math.cos(rz),f=Math.sin(rz);
 return v=>{let x=v[0]*e-v[1]*f,y=v[0]*f+v[1]*e,z=v[2];const y2=y*a-z*b;z=y*b+z*a;y=y2;const x2=x*c+z*d;z=-x*d+z*c;return [x2,y,z];};}
function camera(pos,yaw,pitch,f,cx,cy){const cw=Math.cos(yaw),sw=Math.sin(yaw),cp=Math.cos(pitch),sp=Math.sin(pitch);
 return {pos,f,cx,cy,view(p){let x=p[0]-pos[0],y=p[1]-pos[1],z=p[2]-pos[2];const x2=x*cw-z*sw;z=x*sw+z*cw;const y2=y*cp+z*sp;z=-y*sp+z*cp;return [x2,y2,z];}};}
// A camera that sways sideways while staying aimed at a pivot: the pivot plane holds still,
// nearer things slide one way and farther things the other, the way a stereo image reads.
function swayCam(t,amp,pivotZ,y,f,cx,cy,pitch=0,speed=.33){const x=Math.sin(t*speed)*amp;return camera([x,y,0],Math.atan2(-x,pivotZ),pitch,f,cx,cy);}
// ---------- meshes (triangles with vertex normals) ----------
function sphere(nu,nv){const o=[],P=(i,j)=>{const th=i/nu*Math.PI*2,ph=j/nv*Math.PI;return [Math.sin(ph)*Math.cos(th),Math.cos(ph),Math.sin(ph)*Math.sin(th)];};
 for(let i=0;i<nu;i++)for(let j=0;j<nv;j++){const a=P(i,j),b=P(i+1,j),c=P(i+1,j+1),d=P(i,j+1);if(j>0)o.push({p:[a,b,c],n:[a,b,c]});if(j<nv-1)o.push({p:[a,c,d],n:[a,c,d]});}return o;}
function torus(R,r,nu,nv){const o=[],P=(i,j)=>{const th=i/nu*Math.PI*2,ph=j/nv*Math.PI*2,n=[Math.cos(ph)*Math.cos(th),Math.sin(ph),Math.cos(ph)*Math.sin(th)];return [add([R*Math.cos(th),0,R*Math.sin(th)],mul(n,r)),n];};
 for(let i=0;i<nu;i++)for(let j=0;j<nv;j++){const a=P(i,j),b=P(i+1,j),c=P(i+1,j+1),d=P(i,j+1);o.push({p:[a[0],b[0],c[0]],n:[a[1],b[1],c[1]]},{p:[a[0],c[0],d[0]],n:[a[1],c[1],d[1]]});}return o;}
function box(){const o=[];for(const [ax,s] of [[0,1],[0,-1],[1,1],[1,-1],[2,1],[2,-1]]){const n=[0,0,0];n[ax]=s;const u=[0,0,0],v=[0,0,0];u[(ax+1)%3]=1;v[(ax+2)%3]=1;
 const c=(a,b)=>add(add(n,mul(u,a)),mul(v,b)),A=c(-1,-1),B=c(1,-1),C=c(1,1),D=c(-1,1);o.push({p:[A,B,C],n:[n,n,n]},{p:[A,C,D],n:[n,n,n]});}return o;}
function cone(sides){const o=[],top=[0,1,0];for(let i=0;i<sides;i++){const a=i/sides*Math.PI*2,b=(i+1)/sides*Math.PI*2,A=[Math.cos(a),-1,Math.sin(a)],B=[Math.cos(b),-1,Math.sin(b)],m=(a+b)/2,n=norm([Math.cos(m),.5,Math.sin(m)]),d=[0,-1,0];
 o.push({p:[A,B,top],n:[n,n,n]},{p:[[0,-1,0],B,A],n:[d,d,d]});}return o;}
function place(mesh,rot,scale,pos,extra){const s=Array.isArray(scale)?scale:[scale,scale,scale];
 return mesh.map(t=>Object.assign({p:t.p.map(v=>add(rot([v[0]*s[0],v[1]*s[1],v[2]*s[2]]),pos)),n:t.n.map(rot)},extra));}

// =====================================================================
// PS2 renderer: 480x270, float colour buffer, perspective-correct Gouraud
// colour, depth buffer, distance fog, alpha/additive blending, bloom and
// alternating-field interlace on output. No vertex snapping, no affine wobble.
// =====================================================================
const PW=480,PH=270,PN=PW*PH,pc=new Float32Array(PN*3),pd=new Float32Array(PN);
function pSky(top,bottom,horizon=PH){for(let y=0;y<PH;y++){const c=mix(top,bottom,clamp(y/horizon,0,1));for(let x=0;x<PW;x++){const j=(y*PW+x)*3;pc[j]=c[0];pc[j+1]=c[1];pc[j+2]=c[2];}}pd.fill(1e9);}
function pTris(cam,tris,o={}){
 const fog=o.fog,fn=o.fogNear||0,fr=o.fogRange||1e9,{f,cx:ox,cy:oy}=cam;
 for(const t of tris){
  const A=cam.view(t.p[0]),B=cam.view(t.p[1]),C=cam.view(t.p[2]);if(A[2]<.25||B[2]<.25||C[2]<.25)continue;
  const ax=ox+A[0]*f/A[2],ay=oy-A[1]*f/A[2],bx=ox+B[0]*f/B[2],by=oy-B[1]*f/B[2],cx=ox+C[0]*f/C[2],cy=oy-C[1]*f/C[2];
  const den=(by-cy)*(ax-cx)+(cx-bx)*(ay-cy);if(Math.abs(den)<1e-7)continue;
  const x0=Math.max(0,Math.floor(Math.min(ax,bx,cx))),x1=Math.min(PW-1,Math.ceil(Math.max(ax,bx,cx))),y0=Math.max(0,Math.floor(Math.min(ay,by,cy))),y1=Math.min(PH-1,Math.ceil(Math.max(ay,by,cy)));
  if(x0>x1||y0>y1)continue;
  const ia=1/A[2],ib=1/B[2],ic=1/C[2],ca=t.c[0],cb=t.c[1],cc=t.c[2],al=t.a??1,additive=!!t.add;
  for(let y=y0;y<=y1;y++){const py=y+.5;for(let x=x0;x<=x1;x++){const px=x+.5;
   const l0=((by-cy)*(px-cx)+(cx-bx)*(py-cy))/den;if(l0<0)continue;const l1=((cy-ay)*(px-cx)+(ax-cx)*(py-cy))/den;if(l1<0)continue;const l2=1-l0-l1;if(l2<0)continue;
   const z=1/(l0*ia+l1*ib+l2*ic),i=y*PW+x;if(z>=pd[i])continue;
   const w0=l0*ia*z,w1=l1*ib*z,w2=l2*ic*z;let r=w0*ca[0]+w1*cb[0]+w2*cc[0],g=w0*ca[1]+w1*cb[1]+w2*cc[1],b=w0*ca[2]+w1*cb[2]+w2*cc[2];
   if(fog){const k=clamp((z-fn)/fr,0,1);r+=(fog[0]-r)*k;g+=(fog[1]-g)*k;b+=(fog[2]-b)*k;}
   const j=i*3;if(additive){pc[j]+=r*al;pc[j+1]+=g*al;pc[j+2]+=b*al;}else if(al<1){pc[j]+=(r-pc[j])*al;pc[j+1]+=(g-pc[j+1])*al;pc[j+2]+=(b-pc[j+2])*al;}else{pc[j]=r;pc[j+1]=g;pc[j+2]=b;pd[i]=z;}
  }}
 }
}
function pSplat(x,y,col,k,size=1){const xi=Math.round(x),yi=Math.round(y);for(let dy=-size;dy<=size;dy++)for(let dx=-size;dx<=size;dx++){const X=xi+dx,Y=yi+dy;if(X<0||Y<0||X>=PW||Y>=PH)continue;const w=k/(1+(dx*dx+dy*dy)*1.6),j=(Y*PW+X)*3;pc[j]+=col[0]*w;pc[j+1]+=col[1]*w;pc[j+2]+=col[2]*w;}}
const bloomA=document.createElement('canvas');bloomA.width=120;bloomA.height=68;const bA=bloomA.getContext('2d'),bImg=bA.createImageData(120,68);
const bloomB=document.createElement('canvas');bloomB.width=40;bloomB.height=23;const bB=bloomB.getContext('2d');
function pPresent(ctx,img,frame,{thr=205,amt=.55,scan=.035}={}){
 const d=img.data;for(let y=0;y<PH;y++){const k=((y+frame)&1)?1-scan:1;for(let x=0;x<PW;x++){const i=y*PW+x,j=i*3,p=i*4;d[p]=pc[j]*k;d[p+1]=pc[j+1]*k;d[p+2]=pc[j+2]*k;d[p+3]=255;}}
 ctx.putImageData(img,0,0);
 const b=bImg.data;
 for(let by=0;by<68;by++)for(let bx=0;bx<120;bx++){let r=0,g=0,bl=0,n=0;for(let yy=by*4;yy<Math.min(PH,by*4+4);yy++)for(let xx=bx*4;xx<bx*4+4;xx++){const j=(yy*PW+xx)*3;r+=pc[j];g+=pc[j+1];bl+=pc[j+2];n++;}
  r/=n;g/=n;bl/=n;const s=clamp(((r*.3+g*.55+bl*.15)-thr)/(255-thr),0,1.5),p=(by*120+bx)*4;b[p]=r*s;b[p+1]=g*s;b[p+2]=bl*s;b[p+3]=255;}
 bA.putImageData(bImg,0,0);bB.drawImage(bloomA,0,0,40,23);
 ctx.save();ctx.imageSmoothingEnabled=true;ctx.globalCompositeOperation='lighter';ctx.globalAlpha=amt;ctx.drawImage(bloomA,0,0,PW,PH);ctx.globalAlpha=amt*.9;ctx.drawImage(bloomB,0,0,PW,PH);ctx.restore();
}

// ---------- PS2 1: Glass towers ----------
function towerFaces(x,z,h,w,col){const lo=col.lo,hi=col.hi,top=col.top,P=[[x-w,0,z-w],[x+w,0,z-w],[x+w,0,z+w],[x-w,0,z+w]],Q=P.map(p=>[p[0],h,p[2]]);
 const faces=[];for(let i=0;i<4;i++){const a=P[i],b=P[(i+1)%4],c=Q[(i+1)%4],d=Q[i],ctr=[(a[0]+b[0])/2,h/2,(a[2]+b[2])/2],shade=[.86,1,.9,1.06][i];
  faces.push({ctr,tris:[{p:[a,b,c],c:[mul(lo,shade),mul(lo,shade),mul(hi,shade)],a:col.a},{p:[a,c,d],c:[mul(lo,shade),mul(hi,shade),mul(hi,shade)],a:col.a}]});}
 faces.push({ctr:[x,h,z],tris:[{p:[Q[0],Q[1],Q[2]],c:[top,top,top],a:col.ta},{p:[Q[0],Q[2],Q[3]],c:[top,top,top],a:col.ta}]});return faces;}
function ps2Towers(t){
 const sky=[138,156,218],hor=[208,216,242];pSky(sky,hor,150);
 const cam=camera([Math.sin(t*.07)*1.6,3.1,-3],-Math.sin(t*.07)*.07,.27,320,240,104),camPos=cam.pos,fog={fog:hor,fogNear:8,fogRange:40};
 const towers=[];
 for(let j=0;j<11;j++)for(let i=-7;i<=7;i++){if(Math.abs(i)<=1&&j<6)continue;const x=i*1.55+(j%2)*.4,z=2.5+j*1.55,wave=.5+.5*Math.sin(t*.55+i*.62+j*.41),h=.35+2.6*wave*wave*(1-Math.abs(i)*.03);towers.push({x,z,h,d:Math.hypot(x-camPos[0],z-camPos[2])});}
 towers.sort((a,b)=>b.d-a.d);
 const colReal={lo:[30,44,160],hi:[96,124,228],top:[206,218,255],a:.58,ta:.78},colRefl={lo:[96,112,200],hi:[44,62,170],top:[160,176,236],a:.26,ta:.3};
 const drawTower=(T,col,mirror)=>{const faces=towerFaces(T.x,T.z,mirror?-T.h:T.h,.52,col);faces.sort((a,b)=>Math.hypot(...sub(b.ctr,camPos))-Math.hypot(...sub(a.ctr,camPos)));for(const fc of faces)pTris(cam,fc.tris,fog);};
 for(const T of towers)drawTower(T,colRefl,true);
 const floor=[];for(let z=-1;z<34;z+=2.5)for(let x=-18;x<18;x+=2.5){const k=((Math.round(x/2.5)+Math.round(z/2.5))&1)?1:.965,c=mul([184,194,232],k);floor.push({p:[[x,0,z],[x+2.5,0,z],[x+2.5,0,z+2.5]],c:[c,c,c],a:.7},{p:[[x,0,z],[x+2.5,0,z+2.5],[x,0,z+2.5]],c:[c,c,c],a:.7});}
 pTris(cam,floor,fog);
 for(const T of towers)drawTower(T,colReal,false);
}
// ---------- PS2 2: Light trails (frame feedback) ----------
const trail={n:1500,ready:false};
function ps2Trails(t,dt){
 const top=[16,26,86],bottom=[40,64,168];
 if(!trail.ready){pSky(top,bottom);trail.ready=true;}
 const decay=Math.pow(.86,clamp(dt*30,.3,3));
 for(let y=0;y<PH;y++){const c=mix(top,bottom,y/PH);for(let x=0;x<PW;x++){const j=(y*PW+x)*3;pc[j]=c[0]+(pc[j]-c[0])*decay;pc[j+1]=c[1]+(pc[j+1]-c[1])*decay;pc[j+2]=c[2]+(pc[j+2]-c[2])*decay;}}
 const cols=[[110,160,255],[175,135,255],[100,230,255]];
 for(let i=0;i<trail.n;i++){const s=i%3,ph=(i/trail.n)*Math.PI*2*7.0,a=ph+t*(.22+.06*s)*(s===1?-1:1),r=118+22*Math.sin(3*a+t*.45+s*2)+((i*37)%17-8),tilt=[-.35,.25,.05][s],squash=[.36,.46,.3][s];
  let x=Math.cos(a)*r*1.18,y=Math.sin(a)*r*squash;const xr=x*Math.cos(tilt)-y*Math.sin(tilt),yr=x*Math.sin(tilt)+y*Math.cos(tilt),depth=Math.sin(a),k=(.10+.08*depth)*(.6+.4*Math.sin(i*1.7+t*2));
  pSplat(240+xr,118+yr+s*6,cols[s],k,depth>.4?1:0);}
 for(let i=0;i<40;i++){const a=i*2.39+t*.05,r=60+mod(i*53+t*14,170),x=240+Math.cos(a)*r*1.3,y=118+Math.sin(a)*r*.6;pSplat(x,y,[200,220,255],.25*(.5+.5*Math.sin(t*3+i)),1);}
}
// ---------- PS2 3: Chrome sea ----------
const seaRows=[];for(let k=0,z=1.1;z<60;k++,z*=1.17)seaRows.push(z);
function ps2Sea(t){
 const zen=[132,154,214],hor=[222,224,236],deep=[46,68,132],sunDir=norm([0,.13,1]);
 const cam=camera([Math.sin(t*.05)*1.4,1.35+Math.sin(t*.3)*.04,0],-Math.sin(t*.05)*.04,.06,300,240,118),P=cam.pos;
 pSky(zen,hor,102);
 const sp=cam.view(add(P,mul(sunDir,1000))),sx=cam.cx+sp[0]*cam.f/sp[2],sy=cam.cy-sp[1]*cam.f/sp[2];
 for(let y=0;y<130;y++)for(let x=0;x<PW;x++){const d2=(x-sx)**2+((y-sy)*1.25)**2,g=Math.exp(-d2/700)*.85+Math.exp(-d2/7000)*.22,j=(y*PW+x)*3;pc[j]+=g*(255-pc[j]);pc[j+1]+=g*(246-pc[j+1]);pc[j+2]+=g*(228-pc[j+2]);}
 const h=(x,z)=>.24*Math.sin(.55*x+.42*z-t*.9)+.15*Math.sin(-.33*x+.8*z-t*1.25)+.07*Math.sin(1.6*x+1.15*z-t*2.1);
 const cols=44,V=seaRows.map(z=>{const w=z*1.05+4;return Array.from({length:cols+1},(_,c)=>{const x=(c/cols*2-1)*w+P[0],y=h(x,z),e=.06,n=norm([-(h(x+e,z)-y)/e,1,-(h(x,z+e)-y)/e]),p=[x,y,z],v=norm(sub(p,P)),r=sub(v,mul(n,2*dot(v,n)));
   const skyc=r[1]>0?mix(hor,zen,clamp(r[1]*2.4,0,1)):hor,sun=Math.pow(Math.max(0,dot(r,sunDir)),90)*380+Math.pow(Math.max(0,dot(r,sunDir)),12)*30,fr=.05+.7*Math.pow(1-Math.max(0,-dot(v,n)),5);
   const base=mix(deep,skyc,fr);return {p,c:[base[0]+sun,base[1]+sun*.95,base[2]+sun*.85]};});});
 const tris=[];for(let k=0;k<seaRows.length-1;k++)for(let c=0;c<cols;c++){const a=V[k][c],b=V[k+1][c],cc=V[k+1][c+1],d=V[k][c+1];tris.push({p:[a.p,b.p,cc.p],c:[a.c,b.c,cc.c]},{p:[a.p,cc.p,d.p],c:[a.c,cc.c,d.c]});}
 pTris(cam,tris,{fog:hor,fogNear:14,fogRange:60});
}
// ---------- PS2 4: Pearl orbs ----------
const orbMesh=sphere(22,13),orbSpec=Array.from({length:11},(_,i)=>({r:.55+((i*7)%5)*.12,metal:[.75,.35,.6,.2,.85][i%5],base:[[120,140,220],[214,218,236],[96,110,200],[180,196,240],[150,160,230]][i%5]}));
// ---------- PS2 5: Light shafts ----------
const SX=240,SY=96,shaftR=new Float32Array(PN),shaftA=new Uint16Array(PN),beamLUT=new Float32Array(1024);
for(let y=0;y<PH;y++)for(let x=0;x<PW;x++){const i=y*PW+x;shaftR[i]=Math.hypot(x-SX,(y-SY)*1.15);shaftA[i]=Math.floor(mod(Math.atan2(y-SY,x-SX),Math.PI*2)/(Math.PI*2)*1024)&1023;}
const motes=Array.from({length:170},()=>({x:rnd()*PW,y:rnd()*PH,s:.3+rnd()*.7,p:rnd()*6}));

const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
// The PS2 trails keep their own feedback buffer so two PS2 animations can render during a crossfade.
trail.buf=new Float32Array(PN*3);

// =====================================================================
// SNES and PS1: Codex's ten experiments, copied unchanged from
// experiments.fragment.html into their own scope.
// =====================================================================
const LEGACY=(()=>{const W=320,H=180;
const clamp=(v,a,b)=>Math.max(a,Math.min(b,v)),mod=(v,n)=>(v%n+n)%n,lerp=(a,b,t)=>a+(b-a)*t,mix=(a,b,t)=>a.map((v,i)=>lerp(v,b[i],t));
const bayer=[0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5];
let seed=51;const random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/4294967296;};
const info={
 runway:'Turning perspective tiles, drifting prisms · develops the earlier SNES idea',
 mosaic:'A full-screen tile pattern bends with overlapping waves',
 ribbons:'Layered pixel ribbons scroll at different speeds',
 tunnel:'Octagonal colour bands roll toward you through a pixel tunnel',
 tiles:'Isometric tile clusters float at different depths',
 field:'Camera drift across a textured polygon field · develops the earlier PS1 idea',
 shards:'Three broken polygon rings slowly turn around the folder',
 folds:'Textured ribbons twist and fold in three-dimensional space',
 tide:'A triangulated surface rises and falls, with light moving across its facets',
 frames:'A procession of textured polygon frames passes around the central opening'
};
const reduced=matchMedia('(prefers-reduced-motion: reduce)'),trials=[];
const initialState=window.openai?.widgetState;let saved=(initialState?.modelContent||initialState)?.consoleExperiments;
let picks=Array.isArray(saved?.shortlist)?saved.shortlist.filter(id=>Object.hasOwn(info,id)):[];
let showFolder=saved?.folder!==false;
function save(){window.openai?.setWidgetState?.({modelContent:{consoleExperiments:{shortlist:[...picks],folder:showFolder}},privateContent:null}).catch(()=>{});}
function fillSky(s,top,bottom,horizon=100){const d=s.data;for(let y=0;y<H;y++){const u=Math.round(clamp(y/horizon,0,1)*24)/24,c=mix(top,bottom,u);for(let x=0;x<W;x++){const p=(y*W+x)*4;d[p]=c[0];d[p+1]=c[1];d[p+2]=c[2];d[p+3]=255;}}}
function poly(c,points,col){c.fillStyle=col;c.beginPath();points.forEach(([x,y],i)=>i?c.lineTo(Math.round(x),Math.round(y)):c.moveTo(Math.round(x),Math.round(y)));c.closePath();c.fill();}
const PALETTE=[[238,229,238],[189,175,218],[153,194,185],[229,184,187],[232,210,165],[166,176,209]];
const tileMap=new Uint8ClampedArray(512*512*3);
for(let y=0;y<512;y++)for(let x=0;x<512;x++){
 const tx=x>>5,ty=y>>5,u=x&31,v=y&31;let c=PALETTE[(tx+ty)%2?0:1];
 if(mod(tx,8)===0||mod(ty,8)===0)c=[234,225,241];if(mod(tx+ty*3,13)===3)c=PALETTE[2];if(mod(tx*3+ty,17)===6)c=PALETTE[3];if(mod(tx+ty,19)===8)c=PALETTE[4];
 if(u<1||v<1)c=mix(c,[115,105,155],.13);if(u===2||v===2)c=mix(c,[255,248,239],.3);
 if(((u===6&&v>=6&&v<12)||(v===6&&u>=6&&u<12))&&mod(tx+ty,3)===0)c=mix(c,[255,252,240],.5);
 tileMap.set(c,(y*512+x)*3);
}
function diamond(c,x,y,size,time,color){
 const width=Math.abs(Math.cos(time))*size*.65+size*.25;c.fillStyle='#5e527c28';c.beginPath();c.ellipse(Math.round(x),Math.round(y+size*.85),size*.72,size*.16,0,0,Math.PI*2);c.fill();
 poly(c,[[x,y-size],[x-width,y],[x,y+size],[x+width,y]],color[0]);poly(c,[[x,y-size],[x-width,y],[x,y+size]],color[1]);poly(c,[[x,y-size],[x+width,y],[x,y-size*.13]],color[2]);c.fillStyle='#fff4dc';c.fillRect(Math.round(x-width*.45),Math.round(y-size*.4),2,2);
}
function runway(s,t){
 fillSky(s,[198,191,225],[244,234,229],67);const a=.24*Math.sin(t*.115),ca=Math.cos(a),sa=Math.sin(a);
 for(let y=68;y<H;y++){const z=2600/(y-67),fog=Math.pow(clamp((z-45)/420,0,1),.8)*.95;
 for(let x=0;x<W;x++){const xx=(x-160)*z/175,zz=z+t*16,u=Math.floor(xx*ca-zz*sa+256),v=Math.floor(xx*sa+zz*ca+256),at=(mod(v,512)*512+mod(u,512))*3,p=(y*W+x)*4;for(let k=0;k<3;k++)s.data[p+k]=lerp(tileMap[at+k],[244,234,229][k],fog);}}
 s.ctx.putImageData(s.image,0,0);diamond(s.ctx,34+Math.sin(t*.2)*4,103+Math.sin(t*.35)*2,13,t*.35,['#b97fbb','#9769a5','#e2b4d6']);diamond(s.ctx,285+Math.sin(t*.2+2)*5,88+Math.cos(t*.3)*2,10,t*.35+1,['#74bcae','#529c9c','#b6ddc5']);diamond(s.ctx,308+Math.sin(t*.16)*4,153+Math.sin(t*.28)*2,17,t*.26,['#d1ac73','#b69269','#f1d5a0']);
}
function mosaic(s,t){
 const d=s.data;
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){
  const u=x+9*Math.sin(y*.045+t*.42),v=y+8*Math.sin(x*.033-t*.36),tx=Math.floor(u/12),ty=Math.floor(v/12),gx=mod(u,12),gy=mod(v,12);
  const wave=Math.sin(tx*.33+ty*.49-t*.38),idx=mod(Math.floor((tx+ty)/3),5);let c=PALETTE[idx];
  if(gx<1.1||gy<1.1)c=[240,235,237];else if(gx<2||gy<2)c=mix(c,[255,250,235],.32);
  const light=.13*(wave+1),quiet=Math.exp(-((x-160)**2/3200+(y-72)**2/1700))*.73;c=mix(c,[247,240,234],quiet+light*(1-quiet));
  const p=(y*W+x)*4;s.data[p]=c[0];s.data[p+1]=c[1];s.data[p+2]=c[2];s.data[p+3]=255;
 }s.ctx.putImageData(s.image,0,0);
}
function ribbons(s,t){
 fillSky(s,[232,221,238],[247,235,219],130);s.ctx.putImageData(s.image,0,0);const c=s.ctx;
 const cols=['#c8c4df','#a4c4c6','#84afb2','#c9a6c6','#a38bb8'];
 for(let k=0;k<5;k++){
  const pts=[],base=110+k*12,amp=12+k*3;
  for(let x=-2;x<=322;x+=2){const y=base+Math.sin(x*.019+t*(.12+k*.045)+k*.9)*amp+Math.sin(x*.045-t*.12)*3;pts.push([x,y]);}
  poly(c,[...pts,[322,185],[-2,185]],cols[k]);c.strokeStyle=['#e3deed','#d3e4db','#b6d9cd','#e2c4d8','#c6aed6'][k];c.lineWidth=2;c.beginPath();pts.forEach(([x,y],i)=>i?c.lineTo(x,Math.round(y)):c.moveTo(x,Math.round(y)));c.stroke();
  c.fillStyle='#fff5db80';for(let j=0;j<9;j++){const x=mod(j*43-t*(4+k*3),340)-10,y=base+Math.sin(x*.019+t*(.12+k*.045)+k*.9)*amp+7; c.fillRect(Math.round(x),Math.round(y),8+k*2,1);}
 }
 // A thin counter-moving band at the top balances the deeper waves below.
 for(let x=0;x<W;x+=2){const y=12+Math.sin(x*.025+t*.2)*5;c.fillStyle='#d8c7e3';c.fillRect(x,0,2,Math.round(y));c.fillStyle='#eee2ed';c.fillRect(x,Math.round(y),2,3);}
}
const tunnelCoords=new Float32Array(W*H*2);
for(let y=0;y<H;y++)for(let x=0;x<W;x++){const dx=(x-160)/1.55,dy=y-78,r=Math.max(Math.abs(dx),Math.abs(dy),(Math.abs(dx)+Math.abs(dy))*.707);tunnelCoords[(y*W+x)*2]=r;tunnelCoords[(y*W+x)*2+1]=Math.atan2(dy,dx);}
function tunnel(s,t){
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){
  const i=y*W+x,r=tunnelCoords[i*2],theta=tunnelCoords[i*2+1],phase=160/Math.max(9,r)-t*.17,ring=Math.floor(phase*4),seg=Math.floor((theta+t*.055)*4/Math.PI),edge=mod(phase*4,1);
  let c=PALETTE[mod(ring+seg,6)];if(edge<.12)c=mix(c,[103,99,150],.22);if(edge>.9)c=mix(c,[255,244,221],.65);
  const quiet=1-clamp((r-24)/70,0,1);c=mix(c,[244,234,230],quiet*.95);
  const p=i*4;s.data[p]=c[0];s.data[p+1]=c[1];s.data[p+2]=c[2];s.data[p+3]=255;
 }s.ctx.putImageData(s.image,0,0);
}
const floatTiles=[];for(let j=-4;j<8;j++)for(let i=-7;i<8;i++){const x=160+(i-j)*22,y=86+(i+j)*10;if((x<90||x>235||y>130||y<23)&&x>-40&&x<360&&y>-25&&y<210)floatTiles.push({x,y,i,j,n:mod(i*3+j,5)});}
function tiles(s,t){
 fillSky(s,[219,222,236],[242,236,224],180);s.ctx.putImageData(s.image,0,0);const c=s.ctx;
 for(const a of floatTiles){const lift=4+Math.sin(t*.5+a.i*.6+a.j*.3)*4,x=a.x+Math.sin(t*.12)*5,y=a.y-lift,sz=18,col=PALETTE[a.n],cs=v=>`rgb(${v.join(',')})`;
  poly(c,[[x-sz,a.y+5],[x,a.y+14],[x+sz,a.y+5],[x,a.y-4]],'#766f8c1a');
  poly(c,[[x-sz,y],[x,y+9],[x,y+14],[x-sz,y+5]],cs(mix(col,[74,80,123],.25)));
  poly(c,[[x,y+9],[x+sz,y],[x+sz,y+5],[x,y+14]],cs(mix(col,[74,80,123],.12)));
  poly(c,[[x,y-9],[x+sz,y],[x,y+9],[x-sz,y]],cs(col));
  poly(c,[[x,y-6],[x+12,y],[x,y+6],[x-12,y]],cs(mix(col,[255,247,228],.28)));
  if(a.n===2){c.fillStyle='#fff5da';c.fillRect(Math.round(x)-1,Math.round(y)-1,3,2);}
 }
}
// A shared software rasterizer gives all five PS1 experiments real perspective,
// snapped screen vertices, affine UVs, a depth buffer, and restrained colour dithering.
const tex=Array.from({length:8},(_,n)=>{const out=new Uint8ClampedArray(16*16*3),base=[[124,153,160],[151,164,168],[176,176,184],[157,169,163],[137,151,171],[180,156,175],[185,175,149],[116,164,170]][n];for(let y=0;y<16;y++)for(let x=0;x<16;x++){const d=(random()-.5)*13+((x+y)%7===0?6:0)+(x===0||y===0?-7:0);let col=base.map(v=>v+d);if((x===3&&y>=3&&y<=5)||(y===3&&x>=3&&x<=5))col=base.map(v=>v+20);out.set(col,(y*16+x)*3);}return out;});
function tri(out,a,b,c,texture=0,shade=1){out.push({v:[a,b,c],uv:[[0,15],[0,0],[15,15]],tex:mod(texture,8),shade});}
function quad(out,a,b,c,d,texture=0,shade=1){tri(out,a,b,d,texture,shade*.95);out.push({v:[d,b,c],uv:[[15,15],[0,0],[15,0]],tex:mod(texture,8),shade:shade*1.02});}
const zs=[2.3,2.9,3.6,4.5,5.7,7.2,9.2,12,16,22,32,48];
function elevation(x,z){return clamp((Math.abs(x)-2.6)/6,0,1)*(1.4+.6*Math.cos(z*.35+x*.22))+.15*Math.sin(z*.7+x*.6);}
const fieldMesh=[];
for(let j=0;j<zs.length-1;j++)for(let i=-15;i<15;i++){const x=i*1.5,z=zs[j],zz=zs[j+1];quad(fieldMesh,[x,elevation(x,z),z],[x,elevation(x,zz),zz],[x+1.5,elevation(x+1.5,zz),zz],[x+1.5,elevation(x+1.5,z),z],mod(i*7+j*3,5));}
function raster(s,faces,cam){
 const depths=s.depth;depths.fill(Infinity);const ca=Math.cos(cam.yaw||0),sa=Math.sin(cam.yaw||0),f=cam.f||145,cy=cam.cy??74;
 for(const face of faces){
  const points=face.v.map(v=>{const x=v[0]-(cam.x||0),z=v[2],rx=x*ca-z*sa,rz=x*sa+z*ca;return [Math.round(160+rx*f/rz),Math.round(cy+((cam.y||0)-v[1])*f/rz),rz];});
  if(points.some(p=>p[2]<.6))continue;const [a,b,c]=points,den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1]);if(Math.abs(den)<.01)continue;
  const minX=Math.max(0,Math.min(a[0],b[0],c[0])),maxX=Math.min(W-1,Math.max(a[0],b[0],c[0])),minY=Math.max(0,Math.min(a[1],b[1],c[1])),maxY=Math.min(H-1,Math.max(a[1],b[1],c[1])),texture=tex[face.tex];
  for(let y=minY;y<=maxY;y++)for(let x=minX;x<=maxX;x++){
   const l0=((b[1]-c[1])*(x+.5-c[0])+(c[0]-b[0])*(y+.5-c[1]))/den,l1=((c[1]-a[1])*(x+.5-c[0])+(a[0]-c[0])*(y+.5-c[1]))/den,l2=1-l0-l1;if(l0<0||l1<0||l2<0)continue;
   const z=1/(l0/a[2]+l1/b[2]+l2/c[2]),at=y*W+x;if(z>depths[at])continue;depths[at]=z;
   const u=clamp(Math.floor(l0*face.uv[0][0]+l1*face.uv[1][0]+l2*face.uv[2][0]),0,15),v=clamp(Math.floor(l0*face.uv[0][1]+l1*face.uv[1][1]+l2*face.uv[2][1]),0,15),ti=(v*16+u)*3,fog=clamp((z-(cam.fogNear||5))/(cam.fogRange||29),0,.97),p=at*4;
   for(let k=0;k<3;k++)s.data[p+k]=lerp(texture[ti+k]*face.shade,cam.fog[k],fog);
  }
 }
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){const p=(y*W+x)*4,d=(bayer[(y&3)*4+(x&3)]-7.5)*.32;for(let k=0;k<3;k++)s.data[p+k]=clamp(Math.round((s.data[p+k]+d)*31/255)*255/31,0,255);}
 s.ctx.putImageData(s.image,0,0);
}
function field(s,t){fillSky(s,[188,204,213],[233,233,222],83);raster(s,fieldMesh,{x:Math.sin(t*.17)*.7,y:3.5+Math.sin(t*.14)*.12,yaw:Math.sin(t*.12)*.065,fog:[233,233,222]});}
function transform(p,rx,ry,center){const cx=Math.cos(rx),sx=Math.sin(rx),cy=Math.cos(ry),sy=Math.sin(ry),y=p[1]*cx-p[2]*sx,z=p[1]*sx+p[2]*cx;return [p[0]*cy+z*sy+center[0],y+center[1],-p[0]*sy+z*cy+center[2]];}
function shards(s,t){
 fillSky(s,[206,207,224],[239,231,222],160);const out=[];
 for(let ring=0;ring<3;ring++){
  const centers=[[-6.5,1.8,11],[6.8,1.1,12],[0,-5.3,11]],rad=[3.2,3.7,4.3][ring],rot=t*(.12+ring*.025)+ring*1.1;
  const convert=p=>transform(p,.4+Math.sin(t*.13+ring)*.5,Math.sin(t*.16+ring)*.8,centers[ring]);
  for(let i=0;i<9;i++){if(i%4===2)continue;const a=i/9*Math.PI*2+rot,b=a+.49;
   const p=(r,ang,z)=>convert([Math.cos(ang)*r,Math.sin(ang)*r,z]);
   quad(out,p(rad,a,-.3),p(rad,b,-.3),p(rad+.75,b,-.3),p(rad+.75,a,-.3),ring===0?5:ring===1?7:6,1.04);
   quad(out,p(rad+.75,a,-.3),p(rad+.75,b,-.3),p(rad+.75,b,.4),p(rad+.75,a,.4),ring===0?5:1,.78);
   quad(out,p(rad,a,.4),p(rad,b,.4),p(rad,b,-.3),p(rad,a,-.3),ring===1?7:4,.87);
  }
 }raster(s,out,{y:0,cy:80,fog:[239,231,222],fogNear:15,fogRange:45});
}
function folds(s,t){
 fillSky(s,[205,218,222],[240,232,223],160);const out=[];
 for(let strip=0;strip<3;strip++){
  const point=(u,v)=>{
   const phase=u*.29+t*.27+strip*1.8,z=8+(u+10)*.48;
   if(strip===2)return [u*.85,-4.8+Math.sin(phase)*1.4+v*Math.cos(phase),z+v*Math.sin(phase)];
   const sign=strip?1:-1;return [sign*(6.5+Math.sin(phase)*1.3)+v*Math.cos(phase),u*.65,z+v*Math.sin(phase)];
  };
  for(let j=-10;j<10;j++)for(let k=0;k<3;k++){const v=-1.6+k*1.05;quad(out,point(j,v),point(j+1,v),point(j+1,v+1.05),point(j,v+1.05),strip===0?3:strip===1?5:6,.9+.15*Math.sin(j*.3+t*.27));}
 }raster(s,out,{y:0,cy:79,fog:[240,232,223],fogNear:9,fogRange:55});
}
function tide(s,t){
 fillSky(s,[180,205,219],[225,236,229],80);const out=[];
 const wave=(x,z)=>.45*Math.sin(x*.58+z*.5-t*.65)+.25*Math.cos(z*.9-t*.42)+clamp((Math.abs(x)-4)/9,0,1)*1.2;
 for(let j=0;j<zs.length-1;j++)for(let i=-14;i<14;i++){const x=i*1.5,z=zs[j],zz=zs[j+1],a=wave(x,z),b=wave(x,zz),c=wave(x+1.5,zz),d=wave(x+1.5,z),shine=clamp(1+(a-c)*.24,.72,1.3);quad(out,[x,a,z],[x,b,zz],[x+1.5,c,zz],[x+1.5,d,z],mod(i+j,3)===0?7:mod(i+j,2)===0?0:3,shine);}
 raster(s,out,{x:Math.sin(t*.1)*.35,y:3.1,cy:70,fog:[225,236,229],fogRange:32});
}
function frames(s,t){
 fillSky(s,[204,205,225],[238,233,226],155);const out=[];
 for(let j=0;j<10;j++){
  const z=4+mod(j*3.4-t*.65,34),r=4.7,angle=.09*Math.sin(t*.18+z*.12)+z*.025;
  const point=(rad,a,zz)=>[Math.cos(a+angle)*rad*1.55,Math.sin(a+angle)*rad,zz];
  for(let i=0;i<8;i++){
   const a=i/8*Math.PI*2,b=(i+1)/8*Math.PI*2;
   quad(out,point(r,a,z),point(r,b,z),point(r+.3,b,z),point(r+.3,a,z),j%3===0?5:j%3===1?4:3,1.06);
   quad(out,point(r,a,z),point(r,a,z+.55),point(r,b,z+.55),point(r,b,z),j%3===0?5:4,.78);
  }
 }raster(s,out,{y:0,cy:79,fog:[238,233,226],fogNear:6,fogRange:29});
}
const painters={runway,mosaic,ribbons,tunnel,tiles,field,shards,folds,tide,frames};
return {painters,info};})();

// =====================================================================
// 3DS: 400x225 (3DS top-screen density, 16:9 crop), nearest-neighbour,
// no anti-aliasing. Per-pixel "fragment lighting" in the style of the
// 3DS GPU: a stepped diffuse ramp, a crisp lookup-table specular, Fresnel
// rim light and bump-mapped relief. Every camera sways around a fixed
// pivot plane so near/far parallax reads as stereoscopic depth.
// =====================================================================
const FW=400,FH=225,FN=FW*FH,fc=new Float32Array(FN*3),fd=new Float32Array(FN);
const AQUA=[44,174,194],AQUA_L=[168,226,232],CORAL=[246,150,136],PEARL=[246,248,249];
function fBg(t,top=[208,232,238],bot=[244,247,249]){
 const b1x=110+Math.sin(t*.07)*70,b1y=70+Math.cos(t*.05)*25,b2x=300+Math.cos(t*.06)*60,b2y=150+Math.sin(t*.08)*25;
 for(let y=0;y<FH;y++){const c=mix(top,bot,y/FH);for(let x=0;x<FW;x++){const j=(y*FW+x)*3,g1=Math.exp(-((x-b1x)**2+(y-b1y)**2)/9000)*.4,g2=Math.exp(-((x-b2x)**2+(y-b2y)**2)/7000)*.28;
  fc[j]=c[0]+(178-c[0])*g1+(252-c[0])*g2;fc[j+1]=c[1]+(226-c[1])*g1+(214-c[1])*g2;fc[j+2]=c[2]+(232-c[2])*g1+(204-c[2])*g2;}}
 fd.fill(1e9);
}
function fTris(cam,tris,L,amb=.42){
 const {f,cx:ox,cy:oy,pos:E}=cam;
 for(const t of tris){
  const A=cam.view(t.p[0]),B=cam.view(t.p[1]),C=cam.view(t.p[2]);if(A[2]<.25||B[2]<.25||C[2]<.25)continue;
  const ax=ox+A[0]*f/A[2],ay=oy-A[1]*f/A[2],bx=ox+B[0]*f/B[2],by=oy-B[1]*f/B[2],cx=ox+C[0]*f/C[2],cy=oy-C[1]*f/C[2];
  const den=(by-cy)*(ax-cx)+(cx-bx)*(ay-cy);if(Math.abs(den)<1e-7)continue;
  const x0=Math.max(0,Math.floor(Math.min(ax,bx,cx))),x1=Math.min(FW-1,Math.ceil(Math.max(ax,bx,cx))),y0=Math.max(0,Math.floor(Math.min(ay,by,cy))),y1=Math.min(FH-1,Math.ceil(Math.max(ay,by,cy)));
  if(x0>x1||y0>y1)continue;
  const ia=1/A[2],ib=1/B[2],ic=1/C[2],m=t.m,P0=t.p[0],P1=t.p[1],P2=t.p[2],N0=t.n[0],N1=t.n[1],N2=t.n[2],al=m.alpha??1,base=m.base,rc=m.rimCol||[214,246,250];
  for(let y=y0;y<=y1;y++){const py=y+.5;for(let x=x0;x<=x1;x++){const px=x+.5;
   const l0=((by-cy)*(px-cx)+(cx-bx)*(py-cy))/den;if(l0<0)continue;const l1=((cy-ay)*(px-cx)+(ax-cx)*(py-cy))/den;if(l1<0)continue;const l2=1-l0-l1;if(l2<0)continue;
   const z=1/(l0*ia+l1*ib+l2*ic),i=y*FW+x;if(z>=fd[i])continue;
   const w0=l0*ia*z,w1=l1*ib*z,w2=l2*ic*z;
   let nx=w0*N0[0]+w1*N1[0]+w2*N2[0],ny=w0*N0[1]+w1*N1[1]+w2*N2[1],nz=w0*N0[2]+w1*N1[2]+w2*N2[2];const nl=Math.hypot(nx,ny,nz)||1;nx/=nl;ny/=nl;nz/=nl;
   let vx=E[0]-(w0*P0[0]+w1*P1[0]+w2*P2[0]),vy=E[1]-(w0*P0[1]+w1*P1[1]+w2*P2[1]),vz=E[2]-(w0*P0[2]+w1*P1[2]+w2*P2[2]);const vl=Math.hypot(vx,vy,vz)||1;vx/=vl;vy/=vl;vz/=vl;
   let ndv=nx*vx+ny*vy+nz*vz;if(ndv<0){nx=-nx;ny=-ny;nz=-nz;ndv=-ndv;}
   const d=nx*L[0]+ny*L[1]+nz*L[2],diff=.55*smooth(-.05,.3,d)+.45*clamp(d*.5+.5,0,1);
   let hx=L[0]+vx,hy=L[1]+vy,hz=L[2]+vz;const hl=Math.hypot(hx,hy,hz)||1;const nh=Math.max(0,(nx*hx+ny*hy+nz*hz)/hl);
   const sp=smooth(.3,.6,Math.pow(nh,m.shin||40))*(m.spec??1),rim=Math.pow(1-ndv,2.6)*(m.rim??.5),k=amb+.68*diff;
   const r=base[0]*k+255*sp+rc[0]*rim,g=base[1]*k+255*sp+rc[1]*rim,b=base[2]*k+255*sp+rc[2]*rim,j=i*3;
   if(al<1){const a=clamp(al+sp*.8+rim*.35,0,1);fc[j]+=(r-fc[j])*a;fc[j+1]+=(g-fc[j+1])*a;fc[j+2]+=(b-fc[j+2])*a;}else{fc[j]=r;fc[j+1]=g;fc[j+2]=b;fd[i]=z;}
  }}
 }
}
function fPresent(ctx,img){const d=img.data;for(let i=0;i<FN;i++){const j=i*3,p=i*4;d[p]=fc[j];d[p+1]=fc[j+1];d[p+2]=fc[j+2];d[p+3]=255;}ctx.putImageData(img,0,0);}
const movingLight=t=>norm([Math.cos(t*.3)*.65,.62,-.55+Math.sin(t*.3)*.25]);
function rrect(w,h,r,n=5){const o=[],cs=[[w-r,h-r,0],[-w+r,h-r,Math.PI/2],[-w+r,-h+r,Math.PI],[w-r,-h+r,Math.PI*1.5]];for(const [cx,cy,a0] of cs)for(let k=0;k<=n;k++){const a=a0+k/n*Math.PI/2;o.push([cx+Math.cos(a)*r,cy+Math.sin(a)*r]);}return o;}

// ---------- 3DS 1: Glass panes ----------
// ---------- 3DS 2: Tile wave ----------
const pillow=box().map(t=>({p:t.p,n:t.p.map(v=>norm([v[0],v[1],v[2]*2.4]))}));
function n3Tiles(t){
 fBg(t,[222,236,240],[244,247,249]);const cam=swayCam(t,1.1,11,0,260,200,104),L=movingLight(t*.8),tris=[];
 for(let j=-3;j<=3;j++)for(let i=-6;i<=6;i++){if(Math.abs(i)<=1&&Math.abs(j)<=1)continue;const dist=Math.hypot(i*.75,j),wave=Math.pow(Math.max(0,Math.sin(dist*.75-t*1.25)),3),z=11.5-wave*1.7,s=dist||1;
  const rot=rotator(-j/s*wave*.3,i/s*wave*.3,0),hot=((i*7+j*13)&15)===3,base=hot?CORAL:mix(PEARL,AQUA_L,.25+wave*.75);
  tris.push(...place(pillow,rot,[.56,.56,.14],[i*1.62,j*1.62,z],{m:{base,shin:46,rim:.45}}));}
 fTris(cam,tris,L);
}
// ---------- 3DS 3: Glossy knot ----------
// ---------- 3DS 4: Kinetic curtain ----------
// ---------- 3DS 5: Bump relief ----------

// =====================================================================
// DS: 256x144 (DS screen density, 16:9 crop), nearest-neighbour, 6-bit
// colour channels, low-poly flat shading through a toon table, stepped
// hardware-style linear fog and edge marking (outlines tinted per
// polygon ID). The other half of the DS identity is the stylus.
// =====================================================================
const DW=256,DH=144,DN=DW*DH,dc=new Float32Array(DN*3),dd=new Float32Array(DN),di=new Int16Array(DN),dEdge=[];
const PAPER=[236,239,243],NAVY=[44,56,92],ICE=[106,170,222],SILVER=[196,203,214];
function dFill(top,bot){for(let y=0;y<DH;y++){const c=mix(top,bot,y/DH);for(let x=0;x<DW;x++){const j=(y*DW+x)*3;dc[j]=c[0];dc[j+1]=c[1];dc[j+2]=c[2];}}dd.fill(1e9);di.fill(0);}
function dPaper(grid=8,col=[224,230,238],ruled=false){dFill(PAPER,[230,234,240]);for(let y=0;y<DH;y++)for(let x=0;x<DW;x++){if(ruled?(y%grid===grid-1):(x%grid===0&&y%grid===0)){const j=(y*DW+x)*3;dc[j]=col[0];dc[j+1]=col[1];dc[j+2]=col[2];}}}
function dPlot(x,y,c,a=1){x=Math.round(x);y=Math.round(y);if(x<0||y<0||x>=DW||y>=DH)return;const j=(y*DW+x)*3;dc[j]+=(c[0]-dc[j])*a;dc[j+1]+=(c[1]-dc[j+1])*a;dc[j+2]+=(c[2]-dc[j+2])*a;}
function dLine(x0,y0,x1,y1,c,a=1){x0=Math.round(x0);y0=Math.round(y0);x1=Math.round(x1);y1=Math.round(y1);const dx=Math.abs(x1-x0),dy=-Math.abs(y1-y0),sx=x0<x1?1:-1,sy=y0<y1?1:-1;let e=dx+dy,n=0;
 while(n++<600){dPlot(x0,y0,c,a);if(x0===x1&&y0===y1)break;const e2=2*e;if(e2>=dy){e+=dy;x0+=sx;}if(e2<=dx){e+=dx;y0+=sy;}}}
const DSL=norm([-.5,.72,-.48]);
function dTris(cam,tris,{fog,near,range}){
 const {f,cx:ox,cy:oy,pos:E}=cam;
 for(const t of tris){
  const A=cam.view(t.p[0]),B=cam.view(t.p[1]),C=cam.view(t.p[2]);if(A[2]<.25||B[2]<.25||C[2]<.25)continue;
  const ax=ox+A[0]*f/A[2],ay=oy-A[1]*f/A[2],bx=ox+B[0]*f/B[2],by=oy-B[1]*f/B[2],cx=ox+C[0]*f/C[2],cy=oy-C[1]*f/C[2];
  const den=(by-cy)*(ax-cx)+(cx-bx)*(ay-cy);if(Math.abs(den)<1e-7)continue;
  const x0=Math.max(0,Math.floor(Math.min(ax,bx,cx))),x1=Math.min(DW-1,Math.ceil(Math.max(ax,bx,cx))),y0=Math.max(0,Math.floor(Math.min(ay,by,cy))),y1=Math.min(DH-1,Math.ceil(Math.max(ay,by,cy)));
  if(x0>x1||y0>y1)continue;
  let n=norm(cross(sub(t.p[1],t.p[0]),sub(t.p[2],t.p[0])));if(dot(n,sub(E,t.p[0]))<0)n=mul(n,-1);
  const d=dot(n,DSL),tone=d>.5?1:d>.05?.86:.72,col=mix(mul(t.m.base,tone),[96,108,150],tone<1?.12:0),ia=1/A[2],ib=1/B[2],ic=1/C[2];
  for(let y=y0;y<=y1;y++){const py=y+.5;for(let x=x0;x<=x1;x++){const px=x+.5;
   const l0=((by-cy)*(px-cx)+(cx-bx)*(py-cy))/den;if(l0<0)continue;const l1=((cy-ay)*(px-cx)+(ax-cx)*(py-cy))/den;if(l1<0)continue;const l2=1-l0-l1;if(l2<0)continue;
   const z=1/(l0*ia+l1*ib+l2*ic),i=y*DW+x;if(z>=dd[i])continue;
   const k=Math.floor(clamp((z-near)/range,0,1)*12)/12,j=i*3;dc[j]=lerp(col[0],fog[0],k);dc[j+1]=lerp(col[1],fog[1],k);dc[j+2]=lerp(col[2],fog[2],k);dd[i]=z;di[i]=t.id;
  }}
 }
}
function dPresent(ctx,img,{edge=false}={}){
 const d=img.data,q=v=>Math.round(clamp(v,0,255)*63/255)*255/63;
 for(let y=0;y<DH;y++)for(let x=0;x<DW;x++){const i=y*DW+x,j=i*3,p=i*4;let r=dc[j],g=dc[j+1],b=dc[j+2];
  if(edge&&di[i]){const me=di[i],z=dd[i];if((x>0&&di[i-1]!==me&&dd[i-1]>z)||(x<DW-1&&di[i+1]!==me&&dd[i+1]>z)||(y>0&&di[i-DW]!==me&&dd[i-DW]>z)||(y<DH-1&&di[i+DW]!==me&&dd[i+DW]>z)){const e=dEdge[me]||[110,120,146],k=Math.floor(clamp((z-6)/34,0,1)*12)/12;r=lerp(e[0],r,k*.8);g=lerp(e[1],g,k*.8);b=lerp(e[2],b,k*.8);}}
  d[p]=q(r);d[p+1]=q(g);d[p+2]=q(b);d[p+3]=255;}
 ctx.putImageData(img,0,0);
}
const hash=n=>{const s=Math.sin(n*127.1+311.7)*43758.5453;return s-Math.floor(s);};
// ---------- DS 1: Stylus loops ----------
// ---------- DS 2: Tap ripples ----------
function dsRipples(t){
 dPaper(8);const per=.85,taps=[];
 for(let k=Math.floor((t-3.4)/per);k<=Math.floor(t/per);k++){const tk=k*per;if(tk>t||t-tk>=3.4)continue;const a=hash(k)*Math.PI*2,r=.55+hash(k+.5)*.45;taps.push({x:128+Math.cos(a)*r*118,y:68+Math.sin(a)*r*58,age:t-tk,c:k%4?ICE:[120,136,170]});}
 for(const tp of taps){const R=tp.age*30,fade=.8*Math.pow(1-tp.age/3.4,1.5),x0=Math.max(0,Math.floor(tp.x-R-2)),x1=Math.min(DW-1,Math.ceil(tp.x+R+2)),y0=Math.max(0,Math.floor(tp.y-R-2)),y1=Math.min(DH-1,Math.ceil(tp.y+R+2));
  for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++){const d=Math.hypot(x-tp.x,y-tp.y);for(let r=0;r<3;r++){const rr=R-r*7;if(rr>0&&Math.abs(d-rr)<.55){dPlot(x,y,tp.c,fade*(1-r*.28));break;}}}
  if(tp.age<.3)for(let dy=-2;dy<=2;dy++)for(let dx=-2;dx<=2;dx++)if(dx*dx+dy*dy<=4)dPlot(tp.x+dx,tp.y+dy,tp.c,1-tp.age/.3);}
}
// ---------- DS 3: Fog lattice ----------
const ident=v=>v,boxMesh=box();
function beam(a,b,th,m,id){const c=mul(add(a,b),.5),h=[Math.max(th,Math.abs(b[0]-a[0])/2),Math.max(th,Math.abs(b[1]-a[1])/2),Math.max(th,Math.abs(b[2]-a[2])/2)];return place(boxMesh,ident,h,c,{m,id});}
// ---------- DS 4: Dot tunnel ----------
function dsDots(t){
 const fog=[234,238,244];dFill([228,233,241],fog);const cam=camera([0,0,0],Math.sin(t*.09)*.05,0,200,128,68),travel=mod(t*.9,1);
 for(let zi=0;zi<34;zi++){const z=2.5+zi-travel;if(z<1)continue;for(let xi=-16;xi<=16;xi++){const x=xi*.75;
  for(const side of [-1,1]){const wave=.5*Math.sin(x*.42+t*.9+side)*Math.cos(z*.3-t*.6),y=side*(2.7-wave*side),p=cam.view([x,y,z]);if(p[2]<.5)continue;
   const sx=cam.cx+p[0]*cam.f/p[2],sy=cam.cy-p[1]*cam.f/p[2],k=Math.floor(clamp((z-3)/28,0,1)*12)/12,c=mix(wave>.3?ICE:NAVY,fog,k);dPlot(sx,sy,c,1);if(z<9){dPlot(sx+1,sy,c,1);dPlot(sx,sy+1,c,1);dPlot(sx+1,sy+1,c,1);}}}}
}
// ---------- DS 5: Flipbook doodles ----------
const SHAPES=[a=>[Math.cos(a),Math.sin(a)],a=>{const r=1+.38*Math.cos(5*a);return [r*Math.cos(a)*.8,r*Math.sin(a)*.8];},a=>{const c=Math.cos(a),s=Math.sin(a);return [Math.sign(c)*Math.pow(Math.abs(c),.35)*.9,Math.sign(s)*Math.pow(Math.abs(s),.35)*.9];},a=>{const r=1+.18*Math.cos(8*a);return [r*Math.cos(a)*.9,r*Math.sin(a)*.9];},a=>[.95*Math.sin(a)**3,-(.78*Math.cos(a)-.3*Math.cos(2*a)-.12*Math.cos(3*a)-.05*Math.cos(4*a))]];
function dsFlip(t){
 dPaper(9,[214,226,240],true);const frame=Math.floor(t*8),spots=[[46,52,26,0],[212,50,24,1],[58,112,16,2],[200,110,18,3],[128,16,12,4]];
 spots.forEach(([cx,cy,size,i])=>{const cyc=t*.33+i*.9,A=Math.floor(cyc)%SHAPES.length,B=(A+1)%SHAPES.length,s=smooth(.55,1,cyc%1),bounce=1+.08*Math.sin(t*3+i),rot=Math.sin(t*.4+i)*.3,col=i%2?ICE:NAVY;
  let prev=null;for(let k=0;k<=72;k++){const a=k/72*Math.PI*2,pa=SHAPES[A](a),pb=SHAPES[B](a),x0=lerp(pa[0],pb[0],s),y0=lerp(pa[1],pb[1],s),jx=(hash(frame*7.1+k*.37+i)-.5)*1.4,jy=(hash(frame*3.3+k*.71+i)-.5)*1.4;
   const x=cx+(x0*Math.cos(rot)-y0*Math.sin(rot))*size+jx,y=cy+(x0*Math.sin(rot)+y0*Math.cos(rot))*size/bounce+jy;if(prev)dLine(prev[0],prev[1],x,y,col,1);prev=[x,y];}
  for(let m=0;m<3;m++){const a=hash(frame+m*9+i*31)*Math.PI*2,r=size*1.35+hash(frame*2+m+i)*4;const x=cx+Math.cos(a)*r,y=cy+Math.sin(a)*r;dLine(x,y,x+Math.cos(a)*3,y+Math.sin(a)*3,col,.8);}});
}


// =====================================================================
// Generic engine for the remaining consoles. A target is a float RGB
// buffer plus depth. gOut() applies the console's output stage (bit
// depth, dither, LCD/CRT cell grid, composite blur, LCD ghosting,
// phosphor persistence, bloom) and writes it to the layer's canvas.
// =====================================================================
const GT={},gBayer=[0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5];
function gT(W,H){const k=W+'x'+H;return GT[k]||(GT[k]={W,H,c:new Float32Array(W*H*3),d:new Float32Array(W*H)});}
function hsl(h,s,l){h=mod(h,1);const a=s*Math.min(l,1-l),f=n=>{const k=mod(n+h*12,12);return 255*(l-a*Math.max(-1,Math.min(k-3,9-k,1)));};return [f(0),f(8),f(4)];}
function gClear(T,stops){const {W,H,c}=T;for(let y=0;y<H;y++){const u=H>1?y/(H-1):0;let a=stops[0],b=stops[stops.length-1];for(let i=0;i<stops.length-1;i++)if(u>=stops[i][0]&&u<=stops[i+1][0]){a=stops[i];b=stops[i+1];break;}
 const k=b[0]>a[0]?clamp((u-a[0])/(b[0]-a[0]),0,1):0,r=lerp(a[1][0],b[1][0],k),g=lerp(a[1][1],b[1][1],k),bl=lerp(a[1][2],b[1][2],k);for(let x=0;x<W;x++){const j=(y*W+x)*3;c[j]=r;c[j+1]=g;c[j+2]=bl;}}T.d.fill(1e9);}
function gSet(T,x,y,col){const j=(y*T.W+x)*3;T.c[j]=col[0];T.c[j+1]=col[1];T.c[j+2]=col[2];}
function gBlend(c,j,col,k,add){if(add){c[j]+=col[0]*k;c[j+1]+=col[1]*k;c[j+2]+=col[2]*k;}else{c[j]+=(col[0]-c[j])*k;c[j+1]+=(col[1]-c[j+1])*k;c[j+2]+=(col[2]-c[j+2])*k;}}
function gPix(T,x,y,col,a=1,add=false){x=Math.floor(x);y=Math.floor(y);if(x<0||y<0||x>=T.W||y>=T.H)return;gBlend(T.c,(y*T.W+x)*3,col,a,add);}
function gBox(T,cx,cy,r,pad){return [Math.max(0,Math.floor(cx-r-pad)),Math.min(T.W-1,Math.ceil(cx+r+pad)),Math.max(0,Math.floor(cy-r-pad)),Math.min(T.H-1,Math.ceil(cy+r+pad))];}
function gDisc(T,cx,cy,r,col,a=1,soft=1,add=false,sy=1){const p=soft+1,x0=Math.max(0,Math.floor(cx-r-p)),x1=Math.min(T.W-1,Math.ceil(cx+r+p)),y0=Math.max(0,Math.floor(cy-r*sy-p)),y1=Math.min(T.H-1,Math.ceil(cy+r*sy+p));
 for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++){const d=Math.hypot(x+.5-cx,(y+.5-cy)/sy),cov=soft>0?clamp((r-d)/soft+.5,0,1):(d<=r?1:0);if(cov>0)gBlend(T.c,(y*T.W+x)*3,col,cov*a,add);}}
function gRing(T,cx,cy,r,w,col,a=1,add=false){const [x0,x1,y0,y1]=gBox(T,cx,cy,r+w,1);for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++){const cov=clamp(w/2-Math.abs(Math.hypot(x+.5-cx,y+.5-cy)-r)+.5,0,1);if(cov>0)gBlend(T.c,(y*T.W+x)*3,col,cov*a,add);}}
function gGlow(T,cx,cy,r,col,k){const [x0,x1,y0,y1]=gBox(T,cx,cy,r*2.6,1),r2=r*r;for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++){const g=Math.exp(-((x+.5-cx)**2+(y+.5-cy)**2)/r2)*k;if(g>.003)gBlend(T.c,(y*T.W+x)*3,col,g,true);}}
function gSeg(T,x0,y0,x1,y1,col,w=1,a=1,add=false){const p=w/2+1,xa=Math.max(0,Math.floor(Math.min(x0,x1)-p)),xb=Math.min(T.W-1,Math.ceil(Math.max(x0,x1)+p)),ya=Math.max(0,Math.floor(Math.min(y0,y1)-p)),yb=Math.min(T.H-1,Math.ceil(Math.max(y0,y1)+p)),dx=x1-x0,dy=y1-y0,l2=dx*dx+dy*dy||1e-9,c=T.c;
 for(let y=ya;y<=yb;y++)for(let x=xa;x<=xb;x++){const px=x+.5-x0,py=y+.5-y0,u=clamp((px*dx+py*dy)/l2,0,1),cov=clamp(w/2-Math.hypot(px-u*dx,py-u*dy)+.5,0,1);if(cov>0)gBlend(c,(y*T.W+x)*3,col,cov*a,add);}}
function gStroke(T,pts,col,w=1,a=1,add=false){for(let i=0;i+1<pts.length;i++)gSeg(T,pts[i][0],pts[i][1],pts[i+1][0],pts[i+1][1],col,w,a,add);}
function gPoly(T,pts,col,a=1,add=false){const n=pts.length;let y0=Infinity,y1=-Infinity;for(const p of pts){if(p[1]<y0)y0=p[1];if(p[1]>y1)y1=p[1];}y0=Math.max(0,Math.ceil(y0-.5));y1=Math.min(T.H-1,Math.floor(y1-.5));const xs=[];
 for(let y=y0;y<=y1;y++){const py=y+.5;xs.length=0;for(let i=0;i<n;i++){const p=pts[i],q=pts[(i+1)%n];if((p[1]<=py)!==(q[1]<=py))xs.push(p[0]+(py-p[1])/(q[1]-p[1])*(q[0]-p[0]));}xs.sort((u,v)=>u-v);
  for(let k=0;k+1<xs.length;k+=2){const xa=Math.max(0,Math.ceil(xs[k]-.5)),xb=Math.min(T.W-1,Math.floor(xs[k+1]-.5));for(let x=xa;x<=xb;x++)gBlend(T.c,(y*T.W+x)*3,col,a,add);}}}
const G_L=norm([-.4,.65,-.6]);
// Triangles: t.c = per-vertex colours (Gouraud), t.m = per-pixel lit material, else t.col flat.
function gTris(T,cam,tris,o={}){
 const {W,H,c:C,d:D}=T,{f,cx:ox,cy:oy,pos:E}=cam,fog=o.fog,fn=o.near||0,fr=o.range||1e9,L=o.L||G_L,amb=o.amb??.42;
 for(const t of tris){
  const A=cam.view(t.p[0]),B=cam.view(t.p[1]),Cv=cam.view(t.p[2]);if(A[2]<.25||B[2]<.25||Cv[2]<.25)continue;
  let ax=ox+A[0]*f/A[2],ay=oy-A[1]*f/A[2],bx=ox+B[0]*f/B[2],by=oy-B[1]*f/B[2],cx=ox+Cv[0]*f/Cv[2],cy=oy-Cv[1]*f/Cv[2];if(o.snap){ax=Math.round(ax);ay=Math.round(ay);bx=Math.round(bx);by=Math.round(by);cx=Math.round(cx);cy=Math.round(cy);}
  const den=(by-cy)*(ax-cx)+(cx-bx)*(ay-cy);if(Math.abs(den)<1e-7)continue;
  const x0=Math.max(0,Math.floor(Math.min(ax,bx,cx))),x1=Math.min(W-1,Math.ceil(Math.max(ax,bx,cx))),y0=Math.max(0,Math.floor(Math.min(ay,by,cy))),y1=Math.min(H-1,Math.ceil(Math.max(ay,by,cy)));if(x0>x1||y0>y1)continue;
  const ia=1/A[2],ib=1/B[2],ic=1/Cv[2],al=t.a??1,add=!!t.add,m=t.m,cc=t.c,P=t.p,N=t.n;
  for(let y=y0;y<=y1;y++){const py=y+.5;for(let x=x0;x<=x1;x++){const px=x+.5;
   const l0=((by-cy)*(px-cx)+(cx-bx)*(py-cy))/den;if(l0<0)continue;const l1=((cy-ay)*(px-cx)+(ax-cx)*(py-cy))/den;if(l1<0)continue;const l2=1-l0-l1;if(l2<0)continue;
   const z=1/(l0*ia+l1*ib+l2*ic),i=y*W+x;if(z>=D[i])continue;const w0=l0*ia*z,w1=l1*ib*z,w2=l2*ic*z;let r,g,b;
   if(cc){r=w0*cc[0][0]+w1*cc[1][0]+w2*cc[2][0];g=w0*cc[0][1]+w1*cc[1][1]+w2*cc[2][1];b=w0*cc[0][2]+w1*cc[1][2]+w2*cc[2][2];}
   else if(m){let nx=w0*N[0][0]+w1*N[1][0]+w2*N[2][0],ny=w0*N[0][1]+w1*N[1][1]+w2*N[2][1],nz=w0*N[0][2]+w1*N[1][2]+w2*N[2][2];const nl=Math.hypot(nx,ny,nz)||1;nx/=nl;ny/=nl;nz/=nl;
    let vx=E[0]-(w0*P[0][0]+w1*P[1][0]+w2*P[2][0]),vy=E[1]-(w0*P[0][1]+w1*P[1][1]+w2*P[2][1]),vz=E[2]-(w0*P[0][2]+w1*P[1][2]+w2*P[2][2]);const vl=Math.hypot(vx,vy,vz)||1;vx/=vl;vy/=vl;vz/=vl;
    let ndv=nx*vx+ny*vy+nz*vz;if(ndv<0){nx=-nx;ny=-ny;nz=-nz;ndv=-ndv;}
    const dd=nx*L[0]+ny*L[1]+nz*L[2],k=amb+(1-amb)*(.75*Math.max(0,dd)+.25*(dd*.5+.5)),bs=m.base;r=bs[0]*k;g=bs[1]*k;b=bs[2]*k;
    if(m.env){const ry=2*ndv*ny-vy,e=m.env,lo=ry>0?e[1]:e[1],hi=ry>0?e[2]:e[0],q=ry>0?Math.min(1,ry*1.5):Math.min(1,-ry*2.5),mt=m.metal??.5;r=lerp(r,lerp(lo[0],hi[0],q),mt);g=lerp(g,lerp(lo[1],hi[1],q),mt);b=lerp(b,lerp(lo[2],hi[2],q),mt);}
    let hx=L[0]+vx,hy=L[1]+vy,hz=L[2]+vz;const hl=Math.hypot(hx,hy,hz)||1,sp=Math.pow(Math.max(0,(nx*hx+ny*hy+nz*hz)/hl),m.shin||30)*(m.spec??.8)*255,rim=Math.pow(1-ndv,3)*(m.rim||0),rc=m.rimCol||[255,255,255];
    r+=sp+rc[0]*rim;g+=sp+rc[1]*rim;b+=sp+rc[2]*rim;}
   else if(t.tex){const tu=Math.min(15,Math.max(0,Math.floor(l0*t.uv[0][0]+l1*t.uv[1][0]+l2*t.uv[2][0]))),tv=Math.min(15,Math.max(0,Math.floor(l0*t.uv[0][1]+l1*t.uv[1][1]+l2*t.uv[2][1]))),q=(tv*16+tu)*3,sh=t.shade??1;r=t.tex[q]*sh;g=t.tex[q+1]*sh;b=t.tex[q+2]*sh;}
   else{r=t.col[0];g=t.col[1];b=t.col[2];}
   if(fog){const k=clamp((z-fn)/fr,0,1);r+=(fog[0]-r)*k;g+=(fog[1]-g)*k;b+=(fog[2]-b)*k;}
   const j=i*3;if(add){C[j]+=r*al;C[j+1]+=g*al;C[j+2]+=b*al;}else if(al<1){C[j]+=(r-C[j])*al;C[j+1]+=(g-C[j+1])*al;C[j+2]+=(b-C[j+2])*al;}else{C[j]=r;C[j+1]=g;C[j+2]=b;D[i]=z;}
  }}
 }
}
const G_BLOOM={};
function gOut(L,T,p={}){
 const {W,H,c}=T,s=p.scale||1,OW=W*s,OH=H*s,d=L.img.data;
 if(p.ghost||p.persist){const pv=L.state.prev;if(!pv||pv.length!==c.length)L.state.prev=new Float32Array(c);else{if(p.ghost){const k=p.ghost;for(let i=0;i<c.length;i++)c[i]+=(pv[i]-c[i])*k;}else{const k=p.persist;for(let i=0;i<c.length;i++){const v=pv[i]*k;if(v>c[i])c[i]=v;}}pv.set(c);}}
 if(p.composite){const row=new Float32Array(W*3);for(let y=0;y<H;y++){const o=y*W*3;row.set(c.subarray(o,o+W*3));for(let x=0;x<W;x++)for(let k=0;k<3;k++){const l=row[Math.max(0,x-1)*3+k],m=row[x*3+k],r=row[Math.min(W-1,x+1)*3+k];c[o+x*3+k]=(l+2*m+r)/4;}}}
 const q=p.bits?(1<<p.bits)-1:0,dith=p.dither;
 for(let oy=0;oy<OH;oy++){const y=(oy/s)|0,gy=s>1&&oy%s===s-1;for(let ox=0;ox<OW;ox++){const x=(ox/s)|0,j=(y*W+x)*3;let r=c[j],g=c[j+1],b=c[j+2];
  if(q){const o=dith?(gBayer[(y&3)*4+(x&3)]/16-.5)*255/q:0;r=Math.round(clamp(r+o,0,255)/255*q)/q*255;g=Math.round(clamp(g+o,0,255)/255*q)/q*255;b=Math.round(clamp(b+o,0,255)/255*q)/q*255;}
  if(s>1){if(p.grid==='lcd'&&(gy||ox%s===s-1)){r*=.86;g*=.87;b*=.88;}else if(p.grid==='crt'&&gy){r*=.45;g*=.45;b*=.45;}}
  const k=(oy*OW+ox)*4;d[k]=r;d[k+1]=g;d[k+2]=b;d[k+3]=255;}}
 L.ctx.putImageData(L.img,0,0);
 if(p.bloom){const bw=Math.max(1,W>>2),bh=Math.max(1,H>>2),key=bw+'x'+bh;let B=G_BLOOM[key];
  if(!B){const cv=document.createElement('canvas');cv.width=bw;cv.height=bh;const cx=cv.getContext('2d'),cv2=document.createElement('canvas');cv2.width=Math.max(1,bw/3|0);cv2.height=Math.max(1,bh/3|0);B=G_BLOOM[key]={cv,cx,img:cx.createImageData(bw,bh),cv2,cx2:cv2.getContext('2d')};}
  const bd=B.img.data,thr=p.bloom.thr??200;for(let by=0;by<bh;by++)for(let bx=0;bx<bw;bx++){let r=0,g=0,bl=0,n=0;for(let yy=by*4;yy<Math.min(H,by*4+4);yy++)for(let xx=bx*4;xx<Math.min(W,bx*4+4);xx++){const j=(yy*W+xx)*3;r+=c[j];g+=c[j+1];bl+=c[j+2];n++;}
   r/=n;g/=n;bl/=n;const sv=clamp(((r*.3+g*.55+bl*.15)-thr)/(255-thr),0,1.5),o=(by*bw+bx)*4;bd[o]=r*sv;bd[o+1]=g*sv;bd[o+2]=bl*sv;bd[o+3]=255;}
  B.cx.putImageData(B.img,0,0);B.cx2.drawImage(B.cv,0,0,B.cv2.width,B.cv2.height);
  const ctx=L.ctx;ctx.save();ctx.imageSmoothingEnabled=true;ctx.globalCompositeOperation='lighter';ctx.globalAlpha=p.bloom.amt??.5;ctx.drawImage(B.cv,0,0,OW,OH);ctx.globalAlpha=(p.bloom.amt??.5)*.9;ctx.drawImage(B.cv2,0,0,OW,OH);ctx.restore();}
}

// ---------- NES: 256x144, only NES master-palette colours, 8x8 tiles, 4-colour sub-palettes, no blending ----------
const NC={W:[236,238,236],SKY:[168,204,236],BLU:[76,154,236],DBL:[8,76,196],NAVY:[0,30,116],LAV:[188,188,236],PUR:[120,124,236],DPU:[48,50,236],PNK:[236,180,176],RED:[236,106,100],DRD:[152,34,32],CRM:[228,196,144],GLD:[212,136,32],BRN:[120,60,0],MNT:[160,214,228],TEA:[56,180,204],DTE:[0,102,120]};
const NR=[[NC.W,NC.SKY,NC.BLU,NC.DBL],[NC.W,NC.LAV,NC.PUR,NC.DPU],[NC.W,NC.MNT,NC.TEA,NC.DTE],[NC.W,NC.PNK,NC.RED,NC.DRD],[NC.W,NC.CRM,NC.GLD,NC.BRN]];
function nesStar(T,s){const x=s.x+3,y=s.y+3;if(s.layer===0){gPix(T,x,y,NC.BLU);return;}gPix(T,x,y,NC.W);for(const [dx,dy] of [[1,0],[-1,0],[0,1],[0,-1]])gPix(T,x+dx,y+dy,s.layer===2?NC.SKY:NC.BLU);if(s.layer===2)for(const [dx,dy] of [[2,0],[-2,0],[0,2],[0,-2]])gPix(T,x+dx,y+dy,NC.BLU);}
function nesMeta(T,t){const {W,H}=T,set=[0,1,2,1];
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){const mx=Math.floor(x/16),my=Math.floor(y/16),u=x&15,v=y&15,dist=Math.hypot(mx*16+8-W/2,(my*16+8-H*.43)*1.3)/16,pal=NR[set[mod(Math.floor((dist-t*1.6)/1.6),4)]];
  let idx=1;if(u===0||v===0)idx=0;else if(u===15||v===15||(u===14&&v>1)||(v===14&&u>1))idx=2;else if((u===2&&v>=2&&v<7)||(v===2&&u>=2&&u<7))idx=0;if(dist<2.6)idx=idx===2?1:0;gSet(T,x,y,pal[idx]);}}

// ---------- Game Boy Color: 160x90, 15-bit colour, LCD cell grid and slow-pixel ghosting ----------
const GB={bg:[232,238,218],bg2:[244,244,232],teal:[64,176,156],berry:[214,70,118],sun:[244,196,64],grape:[118,92,198],sky:[104,176,236]};
function gbcBands(T,t){const {W,H}=T,st=[GB.sky,GB.grape,GB.berry,GB.sun,GB.teal];for(let y=0;y<H;y++){const k=mod(Math.floor(y/3)*3/H*.8+t*.04,1)*5,col=mix(mix(st[Math.floor(k)%5],st[(Math.floor(k)+1)%5],k%1),GB.bg2,.5);for(let x=0;x<W;x++)gSet(T,x,y,((x>>3)+(y>>3))&1?mix(col,[255,255,255],.12):col);}}

// ---------- Game Boy Advance: 240x135, 15-bit colour, affine BGs and sprites, alpha blending, mosaic ----------

// ---------- GameCube: smooth 480-line look, indirect-texture warps, fur shells, EFB bloom; indigo palette ----------
const GCC={deep:[22,18,56],ind:[52,44,120],vio:[104,84,200],lil:[176,164,236],amb:[252,196,120]};
function gcCaustics(T,t){const {W,H}=T,hz=Math.floor(H*.16);
 for(let y=0;y<H;y++){if(y<hz){const col=mix(GCC.deep,GCC.ind,y/hz);for(let x=0;x<W;x++)gSet(T,x,y,col);continue;}const zf=26*(H/135)/(y-hz+6),fade=clamp(1-zf*.55,0,1),base=mix(GCC.ind,GCC.vio,fade*.8);
  for(let x=0;x<W;x++){let a=(x-W/2)/(W/240)*zf*.18,b=(zf*2.2+t*.22)*3;a+=.6*Math.sin(b*1.3+t*.7);b+=.6*Math.cos(a*1.1-t*.6);a+=.4*Math.sin(b*1.7-t*.5);const cs=Math.pow(1-Math.abs(Math.sin(a)+Math.sin(b))/2,7)*fade;gSet(T,x,y,[base[0]+cs*200,base[1]+cs*190,base[2]+cs*255]);}}}
function gcFur(T,t){const {W,H}=T;gClear(T,[[0,GCC.ind],[1,GCC.vio]]);
 for(let y=-4;y<H+12;y+=4)for(let x=-4;x<W+4;x+=5){const h=hash(x*.13+y*.71),bx=x+(h-.5)*4,by=y+(hash(y*.3+x)-.5)*3,sway=Math.sin(x*.022+t*1.1)*.7+Math.sin(y*.035-t*.7)*.45+Math.sin((x+y)*.01+t*.4)*.3,len=8+h*5,sh=.5+.5*Math.sin(sway*2.2+x*.01);
  let px=bx,py=by;for(let s=1;s<=3;s++){const k=s/3,nx=bx+Math.sin(sway*k)*len*k,ny=by-Math.cos(sway*k)*len*k*.85;gSeg(T,px,py,nx,ny,mix(GCC.deep,mix(GCC.lil,[255,250,255],sh*.5),k*(.7+.3*sh)),1.1,.9);px=nx;py=ny;}}}
function gcLens(T,t){const {W,H}=T,Ls=[0,1,2,3].map(k=>({x:W/2+Math.cos(t*(.21+k*.04)+k*1.7)*W*.36,y:H*.45+Math.sin(t*(.27+k*.03)+k*2.3)*H*.3,r:H*(.17+k%2*.07)}));
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){let X=x,Y=y,hi=0;for(const l of Ls){const dx=x-l.x,dy=y-l.y,d2=dx*dx+dy*dy,r2=l.r*l.r;if(d2<r2){const k=1-d2/r2;X-=dx*k*.5;Y-=dy*k*.5;hi+=Math.pow(1-k,10)*.5+Math.exp(-((dx+l.r*.35)**2+(dy+l.r*.4)**2)/(r2*.02))*.6;}}
  const gx=Math.min(mod(X,12),12-mod(X,12)),gy=Math.min(mod(Y,12),12-mod(Y,12)),line=Math.max(clamp(1-gx,0,1),clamp(1-gy,0,1)),dt2=gx<1.6&&gy<1.6?1:0;gSet(T,x,y,mix(mix(mix(GCC.ind,GCC.vio,y/H),GCC.lil,line*.45+dt2*.4),[255,255,255],clamp(hi,0,1)));}}

// ---------- Wii: clean white and Wii-blue, soft studio light, pointer and motion ----------
const WI={w:[250,251,252],g:[224,228,233],b:[52,190,237],s:[206,234,247]};
function wiiBg(T){gClear(T,[[0,WI.w],[1,WI.g]]);const {W,H,c}=T;for(let y=0;y<H;y+=4)for(let x=0;x<W;x++){const j=(y*W+x)*3;c[j]*=.988;c[j+1]*=.99;c[j+2]*=.992;}}

// ---------- Mega Drive: 320x180, 9-bit colour (3 bits per channel), line/column scroll, shadow-highlight, dither that the composite blur turns into transparency ----------
const MD={navy:[0,0,72],dk:[0,36,109],blue:[36,72,182],cyan:[72,182,255],teal:[0,145,145],white:[255,255,255],mag:[218,72,182],gold:[255,218,72]};

// ---------- Dreamcast: crisp VGA, bright clean Sega colours, PowerVR translucency and fog, the VMU, online ----------
const DC={or:[240,124,62],w:[250,250,248],blue:[24,96,210],sky:[120,190,240],cr:[252,236,220],gr:[200,206,214]};

// ---------- PSP: native 480x272 widescreen LCD with slight smear, glossy black, a slowly shifting theme hue ----------
const pspHue=t=>.66+.04*Math.sin(t*.03);

// ---------- PS3: HD gloss, HDR bloom, SPU-scale particle counts, motion blur; deep blue-black ----------
function ps3Bg(T){gClear(T,[[0,[4,6,16]],[.6,[18,22,50]],[1,[40,36,82]]]);}

// ---------- Arcade: CRT scanlines and glow, vector monitors, sprite scaling, marquee bulbs, neon ----------
const AR_CUBE={v:[[-1,-1,-1],[1,-1,-1],[1,1,-1],[-1,1,-1],[-1,-1,1],[1,-1,1],[1,1,1],[-1,1,1]],e:[[0,1],[1,2],[2,3],[3,0],[4,5],[5,6],[6,7],[7,4],[0,4],[1,5],[2,6],[3,7]]};
const AR_OCT={v:[[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]].map(v=>mul(v,1.4)),e:[[0,2],[0,3],[0,4],[0,5],[1,2],[1,3],[1,4],[1,5],[2,4],[2,5],[3,4],[3,5]]};
const AR_TET={v:[[1,1,1],[-1,-1,1],[-1,1,-1],[1,-1,-1]],e:[[0,1],[0,2],[0,3],[1,2],[1,3],[2,3]]};
function arcVector(T,t){const {W,H}=T,cam=camera([0,0,0],0,0,W*.68,W/2,H*.43);gClear(T,[[0,[2,4,6]],[1,[4,8,10]]]);
 [[AR_CUBE,[-5.2,1,13],[90,255,150]],[AR_OCT,[5.4,1.6,13],[90,220,255]],[AR_TET,[4.6,-2.6,12],[255,240,140]],[AR_CUBE,[-4.4,-2.8,14],[90,220,255]]].forEach(([S,pos,col],i)=>{const rot=rotator(t*(.5+i*.1),t*(.7-i*.08),i),P=S.v.map(v=>{const q=cam.view(add(rot(v),pos));return [cam.cx+q[0]*cam.f/q[2],cam.cy-q[1]*cam.f/q[2]];});
  for(const [a,b] of S.e)gSeg(T,P[a][0],P[a][1],P[b][0],P[b][1],col,.9,.8,true);for(const p of P)gGlow(T,p[0],p[1],1.2,col,.8);});}

// ---------- Steam: modern native-resolution UI language: dark slate, soft blurred gradients, glass, your library ----------
const ST={dk:[23,29,40],sl:[27,40,56],bl:[102,192,244],lt:[199,213,224]};

// =====================================================================
// Round 5 (after the owner's ratings): "Maybe" animations improved and
// "Don't" animations replaced. What was rated Do: full-frame surfaces
// with depth and texture. What was rated Don't: sparse objects on empty
// backgrounds, plain gradients, loud flat 2D. New work follows the former.
// =====================================================================
const GP=[0,0,0];
// Perspective plane (Mode 7 style): floor below the horizon row, or ceiling above it.
function gPlane(T,o){const {W,H,c}=T,{hz,hgt=1,f=W*.6,yaw=0,x0=0,z0=0,fog,near=0,range=1e9,pat,ceil=false,wob}=o,cw=Math.cos(yaw),sw=Math.sin(yaw),out=GP;
 for(let y=0;y<H;y++){const dy=ceil?hz-(y+.5):(y+.5)-hz;if(dy<=0)continue;const dist=hgt*f/dy,shift=wob?wob(y,dist):0,k=fog?clamp((dist-near)/range,0,1):0;
  for(let x=0;x<W;x++){const lx=(x+.5-W/2+shift)*dist/f,X=x0+lx*cw+dist*sw,Z=z0-lx*sw+dist*cw;pat(X,Z,dist,out,x,y);let r=out[0],g=out[1],b=out[2];if(k){r+=(fog[0]-r)*k;g+=(fog[1]-g)*k;b+=(fog[2]-b)*k;}const j=(y*W+x)*3;c[j]=r;c[j+1]=g;c[j+2]=b;}}}
function zsGeom(z0,z1,r){const o=[];for(let z=z0;z<z1;z*=r)o.push(z);return o;}
function surfRows(hf,zs,nx,xw,xs,cx=0){return zs.map(z=>{const w=xw+z*xs;return Array.from({length:nx+1},(_,i)=>{const x=(i/nx*2-1)*w+cx,y=hf(x,z),e=.04;return {p:[x,y,z],n:norm([-(hf(x+e,z)-y)/e,1,-(hf(x,z+e)-y)/e])};});});}
function gridTris(rows,fn){const o=[];for(let k=0;k+1<rows.length;k++)for(let i=0;i+1<rows[k].length;i++){const a=rows[k][i],b=rows[k+1][i],c=rows[k+1][i+1],d=rows[k][i+1];o.push(fn(a,b,c),fn(a,c,d));}return o;}
const faceN=(a,b,c)=>{const n=norm(cross(sub(b.p,a.p),sub(c.p,a.p)));return n[1]<0?mul(n,-1):n;};
function gTex(base,seed,motif=false){const o=new Float32Array(768);for(let y=0;y<16;y++)for(let x=0;x<16;x++){const n=(hash(seed*31+y*16+x)-.5)*16+((x+y)%7===0?6:0)+(x===0||y===0?-9:0),m=motif&&((x===4&&y>=4&&y<=7)||(y===4&&x>=4&&x<=7))?22:0;for(let k=0;k<3;k++)o[(y*16+x)*3+k]=base[k]+n+m;}return o;}
function texQuad(out,a,b,c,d,tex,shade=1){out.push({p:[a,b,d],uv:[[0,15],[0,0],[15,15]],tex,shade:shade*.95},{p:[d,b,c],uv:[[15,15],[0,0],[15,0]],tex,shade});}

// ---------- NES ----------
function nesFlow(T,t){const {W,H}=T,step=Math.floor(t*6),seq=[0,0,0,1,1,2,1,1],R=NR[0];
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){const mx=x>>4,my=y>>4,u=x&15,v=y&15,dist=Math.hypot(mx*16+8-W/2,(my*16+8-H*.43)*1.3)/16;let idx=dist<2.6?0:seq[mod(mx+my-step,8)];if(u===0||v===0)idx=0;else if((u===15||v===15)&&idx<3)idx++;else if(idx&&u===2&&v>=2&&v<6)idx--;gSet(T,x,y,R[idx]);}}
function nesStars2(T,t){const {W,H}=T,fr=Math.floor(t*30),tw=Math.floor(t*4),rows={};
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){const v=(.5+.5*Math.sin(x*.018+y*.05+t*.12))*(.5+.5*Math.sin(y*.035-x*.006-t*.08)),d=gBayer[(y&3)*4+(x&3)]/16;gSet(T,x,y,v>d*.5+.55?NC.DPU:v*1.5>d?NC.DBL:NC.NAVY);}
 for(let i=0;i<96;i++){const layer=i%3,x=Math.floor(mod(hash(i)*(W+16)-t*[5,11,22][layer],W+16))-8,y=Math.floor(hash(i+.31)*(H-8));(rows[y>>3]=rows[y>>3]||[]).push({x,y,layer:layer===2&&(tw+i)%4===0?1:layer});}
 for(const r in rows){const list=rows[r];list.forEach((s,k)=>{if(list.length>8&&(k+fr)%2)return;nesStar(T,s);});}}
function nesRaster(T,t){const {W,H}=T,hz=Math.floor(H*.4),bands=[NC.LAV,NC.SKY,NC.W,NC.W],curve=Math.sin(t*.25)*40;
 for(let y=0;y<=hz;y++){const k=y/hz*2.99,b=Math.floor(k);for(let x=0;x<W;x++)gSet(T,x,y,bands[k-b>gBayer[(y&3)*4+(x&3)]/16?b+1:b]);}
 for(let y=hz+1;y<H;y++){const z=600/(y-hz),stripe=Math.floor(z*.12+t*3)&1,off=curve/(1+(y-hz)*.08),haze=clamp((z-40)/120,0,1);
  for(let x=0;x<W;x++){const ch=(Math.floor((x-W/2+off)*z/120)&1)^stripe;let col=stripe?(ch?NC.BLU:NC.SKY):(ch?NC.SKY:NC.W);if(gBayer[(y&3)*4+(x&3)]/16<haze)col=NC.LAV;gSet(T,x,y,col);}}}

// ---------- SNES ----------
const SP=[[238,229,238],[189,175,218],[153,194,185],[229,184,187],[232,210,165],[166,176,209]];
function snesMosaic2(T,t){const {W,H}=T,pick=[0,1,0,5,0,2,0,3,0,4];
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){const u=x+5*Math.sin(y*.03+t*.35),v=y+4*Math.sin(x*.025-t*.3),tx=Math.floor(u/16),ty=Math.floor(v/16),gx=mod(u,16),gy=mod(v,16);let c=SP[pick[mod(tx+ty*3,10)]];
  if(gx<1.5||gy<1.5)c=mix(c,[255,250,240],.45);else if(gx>14.5||gy>14.5)c=mix(c,[110,100,150],.18);const light=.12*(Math.sin(tx*.4+ty*.5-t*.5)+1),quiet=Math.exp(-((x-W/2)**2/3200+(y-H*.43)**2/1700))*.7;gSet(T,x,y,mix(mix(c,[255,252,244],light),[246,238,236],quiet));}}
function snesLake(T,t){const {W,H}=T,hz=Math.floor(H*.42),wc=[[166,176,209],[189,175,218],[153,194,185],[220,226,240],[238,229,238]];for(let y=0;y<=hz;y++){const c=mix([198,191,225],[244,234,229],Math.round(y/hz*8)/8);for(let x=0;x<W;x++)gSet(T,x,y,c);}
 gPlane(T,{hz,hgt:1,f:175,yaw:.18*Math.sin(t*.1),z0:t*1.2,fog:[244,234,229],near:8,range:60,wob:(y)=>Math.sin(y*.6+t*3)*(y-hz)*.03,pat:(X,Z,d,o)=>{const w=Math.sin(X*.9+Math.sin(Z*.5+t*.8)*1.2+t*.6)+Math.sin(Z*1.3-t*.9);let r=wc[clamp(Math.floor((w+2)*1.2),0,4)];const cx=Math.floor(X*2),cz=Math.floor(Z*2);if(hash(cx*7.3+cz*1.7)>.985&&mod(t*3+hash(cx+cz*13),1)<.35)r=[255,252,240];o[0]=r[0];o[1]=r[1];o[2]=r[2];}});}

// ---------- Game Boy Color ----------
function gbcBandsBg(T,t,lift=.5){const {W,H}=T,st=[GB.sky,GB.grape,GB.berry,GB.sun,GB.teal];for(let y=0;y<H;y++){const k=mod(Math.floor(y/3)*3/H*.8+t*.04,1)*5,col=mix(mix(st[Math.floor(k)%5],st[(Math.floor(k)+1)%5],k%1),GB.bg2,lift);for(let x=0;x<W;x++)gSet(T,x,y,((x>>3)+(y>>3))&1?mix(col,[255,255,255],.12):col);}}
function gbcSheen2(T,t){const {W,H}=T;gbcBandsBg(T,t,.6);for(let y=0;y<H;y++)for(let x=0;x<W;x++){const u=mod(x,12)-5.5,v=mod(y,12)-5.5,j=(y*W+x)*3,dia=Math.abs(u)+Math.abs(v);let k=dia<4?(dia>3?-.18:.22):0;const s=mod(x*.7+y-t*22,220);if(s<18)k+=.35*(1-Math.abs(s-9)/9);if(k>0)gBlend(T.c,j,[255,255,255],k,false);else if(k<0)gBlend(T.c,j,[60,70,90],-k,false);}}
function gbcConfetti2(T,t){const {W,H}=T,cols=[GB.teal,GB.berry,GB.sun,GB.grape,GB.sky];gbcBandsBg(T,t,.8);
 for(let i=0;i<110;i++){const near=i%4===0,x=Math.floor(mod(hash(i)*W+Math.sin(t*1.3+i)*(near?10:5),W+6))-3,y=Math.floor(mod(hash(i+.7)*H+t*(near?20:9+hash(i+.2)*8),H+6))-3,ph=mod(Math.floor(t*6+i),4),c=cols[i%5];
  if(near){gPix(T,x,y,c);gPix(T,x+1,y,c);gPix(T,x,y+1,c);gPix(T,x+1,y+1,mix(c,[0,0,0],.2));}else{gPix(T,x,y,c);if(ph===0)gPix(T,x+1,y,c);else if(ph===2)gPix(T,x,y+1,c);}}}
function gbcAttr(T,t){const {W,H}=T,pals=[GB.sky,GB.grape,GB.teal,GB.berry].map(c=>[GB.bg2,mix(c,GB.bg2,.5),c]);
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){const tx=x>>3,ty=y>>3,u=x&7,v=y&7,dist=Math.hypot(tx*8+4-W/2,(ty*8+4-H*.45)*1.3)/8,P=pals[mod(Math.floor((dist-t*2)/2.2),4)];let idx=1;if(u===0||v===0)idx=0;else if(u===7||v===7)idx=2;else if(u===2&&v===2)idx=0;if(dist<3.4)idx=idx===2?1:0;gSet(T,x,y,P[idx]);}}

// ---------- Game Boy Advance (new set; calmer violet/cream palette, all with depth) ----------
const GV={night:[46,40,92],vio:[112,101,167],lav:[178,170,230],cream:[248,240,220],teal:[96,176,190],pink:[236,160,190]};
function gbaVoxel(T,t){const {W,H,c}=T,hz=H*.36,camZ=t*7,camX=Math.sin(t*.08)*30,camH=26,yb=new Float32Array(W).fill(H),sky=[238,228,240];gClear(T,[[0,[150,140,214]],[.36,sky],[1,sky]]);
 const hgt=(X,Z)=>9*Math.sin(X*.045)*Math.cos(Z*.035)+6*Math.sin(X*.1+Z*.07)+4*Math.cos(Z*.12-X*.03)+Math.min(18,Math.abs(X-camX)*.22);
 for(let z=3,dz=.6;z<150;z+=dz,dz*=1.025){const fog=clamp((z-30)/110,0,1);for(let x=0;x<W;x++){const X=camX+(x-W/2)*z/(W*.55),h=hgt(X,camZ+z),sy=hz+(camH-h)*H*.9/z;if(sy<yb[x]){const k=clamp(h/30,0,1),sl=clamp((hgt(X,camZ+z-1.5)-h)*.15+.5,0,1),col=mix(mix(mix(mix(GV.vio,GV.lav,k),GV.cream,Math.max(0,k-.6)*2),[90,80,150],sl*.3),sky,fog);
  for(let y=Math.max(0,Math.floor(sy));y<Math.min(H,Math.ceil(yb[x]));y++){const j=(y*W+x)*3;c[j]=col[0];c[j+1]=col[1];c[j+2]=col[2];}yb[x]=sy;}}}}
function gbaCorridor(T,t){const {W,H,c}=T,f=W*.55,hz=H*.43,camZ=t*2.2,fog=[60,52,110];
 for(let x=0;x<W;x++){const dx=(x+.5-W/2)/f,zw=2/Math.max(1e-3,Math.abs(dx)),top=hz-1.3*f/zw,bot=hz+f/zw,side=dx<0?0:1;
  for(let y=0;y<H;y++){let z,col;if(y+.5<top){z=1.3*f/(hz-y-.5);col=(mod(camZ+z,4)<.3&&Math.abs(dx*z)<1.4)?GV.cream:GV.night;}
   else if(y+.5>bot){z=f/(y+.5-hz);col=(Math.floor(dx*z)+Math.floor(camZ+z))&1?GV.lav:mix(GV.lav,GV.cream,.6);}
   else{z=zw;const u=camZ+zw,v=(y+.5-top)/(bot-top),m=mod(u,2),seam=m<.1||v<.04||v>.96,win=hash(Math.floor(u/2)*3.1+side)>.6&&v>.3&&v<.62&&m>.4&&m<1.6;col=seam?GV.night:win?GV.teal:v<.5?GV.vio:mix(GV.vio,GV.lav,.35);}
   col=mix(col,fog,clamp((z-3)/22,0,1));const j=(y*W+x)*3;c[j]=col[0];c[j+1]=col[1];c[j+2]=col[2];}}}
function gbaIso(T,t){const {W,H}=T,N=22;gClear(T,[[0,GV.lav],[1,GV.cream]]);
 for(let s=0;s<2*N;s++)for(let i=0;i<N;i++){const j=s-i;if(j<0||j>=N)continue;const x=W/2+(i-j)*12,yb=-70+(i+j)*6,calm=smooth(30,90,Math.hypot(x-W/2,(yb-H*.43)*1.6)),wv=.5+.5*Math.sin(i*.5+j*.35-t*1.2),h=3+16*wv*calm,y=yb-h;
  gPoly(T,[[x-12,y],[x,y+6],[x,yb+6],[x-12,yb]],mix(GV.vio,GV.lav,.25));gPoly(T,[[x,y+6],[x+12,y],[x+12,yb],[x,yb+6]],mix(GV.vio,GV.lav,.55));gPoly(T,[[x,y-6],[x+12,y],[x,y+6],[x-12,y]],mix(GV.lav,GV.cream,wv*calm));}}
function gbaTwist(T,t){const {W,H,c}=T,hz=H*.4,f=W*.55;gClear(T,[[0,[160,150,220]],[.4,GV.cream],[1,GV.cream]]);
 for(let y=Math.floor(hz)+1;y<H;y++){const dist=f/(y+.5-hz),yaw=t*.08+Math.sin(t*.25)*dist*.06,cy=Math.cos(yaw),sy=Math.sin(yaw),fog=clamp((dist-4)/26,0,1);
  for(let x=0;x<W;x++){const lx=(x+.5-W/2)*dist/f,X=lx*cy+dist*sy,Z=-lx*sy+dist*cy+t*1.2,r=Math.max(Math.abs(mod(X,2)-1),Math.abs(mod(Z,2)-1)),ring=Math.floor(Math.hypot(Math.floor(X/2),Math.floor(Z/2)))%3,col=mix(r>.88?GV.vio:r<.35?[GV.pink,GV.teal,GV.lav][ring]:GV.cream,GV.cream,fog),j=(y*W+x)*3;c[j]=col[0];c[j+1]=col[1];c[j+2]=col[2];}}}

// ---------- GameCube ----------
function gcField(T,t){const {W,H}=T,cam=camera([0,3.6,-1],0,.32,W*.62,W/2,H*.4),tris=[];gClear(T,[[0,GCC.deep],[1,GCC.ind]]);
 for(let j=0;j<11;j++)for(let i=-7;i<=7;i++){const w=.5+.5*Math.sin(i*.55+j*.42-t*1.1),h=.25+1.6*w*w,hot=hash(i*13+j*7)>.93;tris.push(...place(boxMesh,ident,[.52,h/2,.52],[i*1.25,h/2-.8,3+j*1.25],{m:{base:hot?GCC.amb:mix(GCC.vio,GCC.lil,w*.6),spec:.9,shin:40,rim:.45,rimCol:[200,190,255]}}));}
 gTris(T,cam,tris,{amb:.3,fog:GCC.deep,near:6,range:16});}

// ---------- DS (uses the DS buffers and helpers) ----------
function dsRippleFloor(t){dFill(PAPER,[230,234,240]);const cam=camera([0,2.2,0],0,.16,200,128,60),taps=[];
 for(let k=Math.floor((t-5)/.9);k<=Math.floor(t/.9);k++){const tk=k*.9;if(tk>t||t-tk>=5)continue;taps.push([(hash(k)-.5)*16,6+hash(k+.5)*18,t-tk]);}
 const h=(x,z)=>{let s=0;for(const [tx,tz,age] of taps){const w=Math.hypot(x-tx,z-tz)-age*3.2;if(w<0&&w>-4)s+=Math.sin(w*2.4)*.35*(1-age/5)*(1+w/4);}return s;};
 const plot=(x,z)=>{const y=h(x,z),p=cam.view([x,y,z]);if(p[2]<.5)return;dPlot(cam.cx+p[0]*cam.f/p[2],cam.cy-p[1]*cam.f/p[2],mix(y>.08?ICE:NAVY,PAPER,Math.floor(clamp((z-3)/30,0,1)*12)/12*.9),1);};
 for(let xi=-16;xi<=16;xi++)for(let z=2;z<34;z+=.12)plot(xi,z);for(let zi=2;zi<34;zi++)for(let x=-24;x<=24;x+=.1)plot(x,zi);}

// ---------- Wii (new set: soft white surfaces) ----------
function wiiPointer2(T,t){const {W,H}=T,P=s=>[W/2+W*.4*Math.sin(s*.37)+W*.08*Math.sin(s*1.1),H*.45+H*.3*Math.sin(s*.53+1)+H*.07*Math.cos(s*1.3)],p=P(t),sp=W/40,clicks=[];wiiBg(T);
 for(let k=0;k<3;k++){const tk=(Math.floor(t/2.2)-k)*2.2,age=t-tk;if(age>=0&&age<3)clicks.push([...P(tk),age]);}
 for(let gy=sp/2;gy<H;gy+=sp)for(let gx=sp/2;gx<W;gx+=sp){const dx=gx-p[0],dy=gy-p[1],d=Math.hypot(dx,dy)||1,push=Math.exp(-d*d/(W*W*.006))*sp*.9;let x=gx+dx/d*push,y=gy+dy/d*push,hot=Math.exp(-d*d/(W*W*.01));
  for(const [cx,cy,age] of clicks){const ex=gx-cx,ey=gy-cy,e=Math.hypot(ex,ey)||1,w=e-age*W*.12;if(Math.abs(w)<sp*1.5){const s=Math.cos(w/(sp*1.5)*Math.PI/2)*(1-age/3);x+=ex/e*s*sp*.5;y+=ey/e*s*sp*.5;hot=Math.max(hot,s*.8);}}
  gDisc(T,x,y,1.3+hot*1.4,mix([196,204,214],WI.b,hot),.9,1);}
 gDisc(T,p[0],p[1],7,WI.b,.85,1.5);gDisc(T,p[0],p[1],3.2,[255,255,255],1);}
function wiiTiles(T,t){const {W,H}=T;gClear(T,[[0,WI.w],[.3,[240,244,248]],[1,WI.g]]);
 gPlane(T,{hz:H*.3,hgt:1.4,f:W*.6,yaw:Math.sin(t*.07)*.12,z0:t*.5,fog:[244,247,250],near:6,range:30,pat:(X,Z,d,o)=>{const u=mod(X,1.2)-.6,v=mod(Z,1.2)-.6,inT=Math.hypot(Math.max(0,Math.abs(u)-.38),Math.max(0,Math.abs(v)-.38))<.14,sweep=Math.exp(-((mod(X*.4+Z-t*3,26)-13)**2)/3);let c=inT?mix([244,247,250],[255,255,255],clamp(.5-v,0,1)*.5):[206,222,232];c=mix(c,inT?[200,234,250]:WI.b,sweep*.55);o[0]=c[0];o[1]=c[1];o[2]=c[2];}});}
function wiiStripes(T,t){const {W,H,c}=T,s=W/480;
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){const X=x/s,Y=y/s,a=X*.012+t*.4,b=Y*.01-t*.25,w=Math.sin(a)*Math.sin(b),slope=Math.cos(a)*.012*Math.sin(b)*14,stripe=mod(Y+w*14,7)<1.6;let col=mix(WI.w,WI.g,Y/(H/s));if(stripe)col=mix(col,[205,214,224],.55);col=mul(col,clamp(1+slope*.9+w*.05,.85,1.06));if(w>.6)col=mix(col,WI.s,(w-.6)*.8);const j=(y*W+x)*3;c[j]=col[0];c[j+1]=col[1];c[j+2]=col[2];}}

// ---------- 3DS (uses the fragment-lit 3DS renderer) ----------

// ---------- Mega Drive (new set: surfaces in 9-bit colour) ----------
function mdSpeed2(T,t){const {W,H}=T,hz=Math.floor(H*.45);gClear(T,[[0,MD.navy],[.45,MD.blue],[1,MD.blue]]);
 for(let y=hz+1;y<H;y++){const z=500/(y-hz),stripe=Math.floor(z*.2+t*14)&1,curve=Math.sin(t*.4)*60/(1+(y-hz)*.1);for(let x=0;x<W;x++){const ck=(Math.floor((x-W/2+curve)*z/100)&1)^stripe;gSet(T,x,y,ck?(stripe?MD.cyan:MD.blue):(stripe?MD.blue:MD.dk));}}
 for(let i=0;i<48;i++){const y=Math.floor(hash(i)*hz),v=120+hash(i+.3)*320,len=v*.12,head=mod(hash(i+.6)*(W+200)-t*v,W+200)-60;for(let k=0;k<len;k++){const x=Math.floor(head+k);if(x<0||x>=W)continue;const a=1-k/len;gPix(T,x,y,a>.8?MD.white:MD.cyan,a);}}}
function mdPlanes(T,t){const {W,H}=T,hz=H*.43;gClear(T,[[0,MD.dk],[1,MD.dk]]);
 for(const ceil of [false,true])for(let y=0;y<H;y++){const dy=ceil?hz-y-.5:y+.5-hz;if(dy<=0)continue;const z=(ceil?700:500)/dy,off=Math.sin(t*.5+z*.02)*30/(1+dy*.08)+t*40*(ceil?-.5:1),stripe=Math.floor(z*.1+t*(ceil?2:5))&1;
  for(let x=0;x<W;x++){const ck=(Math.floor((x-W/2+off)*z/120)&1)^stripe;gSet(T,x,y,z>60&&(x+y)&1?MD.dk:ceil?(ck?MD.teal:MD.dk):(ck?MD.blue:MD.navy));}}}
function mdPipe(T,t){const {W,H}=T,f=W*.55,cy=H*.43;gClear(T,[[0,MD.navy],[.43,MD.blue],[1,MD.blue]]);
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){const dx=(x+.5-W/2)/f,dy=-(y+.5-cy)/f;if(dy>-.05)continue;const z=2.2/Math.hypot(dx,dy),ang=Math.atan2(dy,dx)+Math.sin(z*.08-t*.3)*.4,q=ang*8/Math.PI,ck=(Math.floor(q)+Math.floor(z*.5+t*6))&1;let col=mod(q,1)<.08?MD.white:ck?MD.cyan:MD.blue;if(z>30&&(x+y)&1)col=MD.dk;gSet(T,x,y,col);}}

// ---------- Dreamcast ----------
function dcFog2(T,t){const {W,H}=T,fog=[252,232,214],cam=camera([0,2.6,0],0,.18,W*.62,W/2,H*.43),tris=[];gClear(T,[[0,[250,212,186]],[.45,fog],[1,fog]]);
 for(let j=0;j<12;j++)for(let i=-7;i<=7;i++){const z=mod(j*1.5-t*1.4,18)+3,w=.5+.5*Math.sin(i*.6+j*.9+t*.8),h=.3+1.4*w;tris.push(...place(boxMesh,ident,[.55,h/2,.55],[i*1.5,h/2-1,z],{m:{base:[DC.w,DC.or,DC.w,DC.sky][mod(i+j,4)],spec:.4,shin:20,rim:.15}}));}
 gTris(T,cam,tris,{fog,near:5,range:16,amb:.5});}
function dcSea(T,t){const {W,H}=T,cam=camera([0,1.6,0],0,.08,W*.62,W/2,H*.42),L=norm([.3,.7,.6]),Hh=norm(add(L,[0,.3,-1]));gClear(T,[[0,[40,120,220]],[.4,[150,210,250]],[1,[150,210,250]]]);
 const hf=(x,z)=>.3*Math.sin(x*.6+z*.4-t*1.1)+.18*Math.sin(-x*.4+z*.9-t*1.5);
 gTris(T,cam,gridTris(surfRows(hf,zsGeom(1.2,50,1.15),36,4,1),(a,b,c)=>{const n=faceN(a,b,c),d=Math.max(0,dot(n,L)),sp=Math.pow(Math.max(0,dot(n,Hh)),40);return {p:[a.p,b.p,c.p],col:[20+d*40+sp*235,130+d*70+sp*125,190+d*50+sp*65]};}),{fog:[170,218,250],near:12,range:40});}
function dcChecker(T,t){const {W,H}=T,hz=H*.38;gClear(T,[[0,[30,110,220]],[.38,[170,220,250]],[1,[170,220,250]]]);
 gPlane(T,{hz,hgt:1.5,f:W*.6,yaw:t*.04,z0:t,fog:[236,240,244],near:10,range:40,pat:(X,Z,d,o)=>{const c=(Math.floor(X/1.5)+Math.floor(Z/1.5))&1?DC.or:DC.w;o[0]=c[0];o[1]=c[1];o[2]=c[2];}});
 for(let y=Math.floor(hz)+1;y<H;y++){const k=.18*Math.exp(-(y-hz)/14);for(let x=0;x<W;x++)gBlend(T.c,(y*W+x)*3,[200,230,255],k,false);}}

// ---------- PlayStation (new pair in Codex's style: affine textures, snapped vertices, 15-bit dither) ----------
const PST=[gTex([124,153,160],1,true),gTex([151,164,168],2),gTex([176,176,184],3,true),gTex([157,169,163],4),gTex([185,175,149],5,true),gTex([137,151,171],6)];
function ps1Terrace(T,t){const {W,H}=T,fog=[233,233,222],cam=camera([Math.sin(t*.15)*.8,4.2,-1],Math.sin(t*.1)*.06,.36,W*.55,W/2,H*.4),tris=[];gClear(T,[[0,[188,204,213]],[.5,fog],[1,fog]]);
 for(let j=0;j<11;j++)for(let i=-8;i<=8;i++){const x=i*1.4,z=2+j*1.4,lv=Math.floor(2+2*Math.sin(i*.5+t*.25)+1.5*Math.cos(j*.6-t*.2)),h=Math.abs(i)<=1&&j<4?.05:Math.max(.05,lv*.35),y0=-1,y1=y0+h,a=[x-.7,y1,z-.7],b=[x-.7,y1,z+.7],c=[x+.7,y1,z+.7],d=[x+.7,y1,z-.7],sx=x<0?x+.7:x-.7;
  texQuad(tris,a,b,c,d,PST[mod(i*3+j,6)],1);texQuad(tris,[x-.7,y0,z-.7],a,d,[x+.7,y0,z-.7],PST[mod(i+j*2,6)],.72);texQuad(tris,[sx,y0,z-.7],[sx,y1,z-.7],[sx,y1,z+.7],[sx,y0,z+.7],PST[mod(i+j,6)],.82);}
 gTris(T,cam,tris,{fog,near:6,range:18,snap:true});}
function ps1Pipe(T,t){const {W,H}=T,fog=[233,233,222],cam=camera([0,0,0],0,0,W*.55,W/2,H*.43),tris=[],R=3.2,N=12,travel=mod(t*2,1.6),row0=Math.floor(t*2/1.6);gClear(T,[[0,fog],[1,fog]]);
 const ctr=z=>[Math.sin(z*.08+t*.2)*1.6,Math.cos(z*.06+t*.15)*.9],P=(c,a,z)=>[c[0]+Math.cos(a)*R*1.5,c[1]+Math.sin(a)*R,z];
 for(let k=0;k<16;k++){const z0=1+k*1.6-travel,z1=z0+1.6,c0=ctr(z0+t*2),c1=ctr(z1+t*2);for(let i=0;i<N;i++){const a0=i/N*Math.PI*2,a1=(i+1)/N*Math.PI*2;texQuad(tris,P(c0,a0,z0),P(c1,a0,z1),P(c1,a1,z1),P(c0,a1,z0),PST[mod(i+(k+row0)*5,6)],.8+.2*Math.cos(a0-1));}}
 gTris(T,cam,tris,{fog,near:4,range:20,snap:true});}

// ---------- PS2 (uses the PS2 renderer) ----------
function orbShade(o,n,p,r,cam,L,Hh){const v=norm(sub(add(p,mul(n,r)),cam.pos)),diff=Math.max(0,dot(n,L)),spec=Math.pow(Math.max(0,dot(n,Hh)),48),rr=sub(v,mul(n,2*dot(v,n))),env=rr[1]>0?mix([214,224,250],[120,140,214],clamp(rr[1]*1.6,0,1)):mix([226,226,236],[80,88,140],clamp(-rr[1]*3,0,1)),rim=Math.pow(1-Math.max(0,-dot(v,n)),3),lit=mix(mul(o.base,.45+.65*diff),env,o.metal);return [lit[0]+spec*230+rim*70,lit[1]+spec*230+rim*70,lit[2]+spec*220+rim*80];}
function ps2Orbs2(t){const flo=[214,220,242];pSky([190,200,236],flo,PH*.62);for(let y=0;y<PH;y++)for(let x=0;x<PW;x++){const g=Math.exp(-((x-240)**2/14000+(y-100)**2/5200))*.5,j=(y*PW+x)*3;pc[j]+=g*(255-pc[j]);pc[j+1]+=g*(255-pc[j+1]);pc[j+2]+=g*(255-pc[j+2]);}
 const cam=camera([0,1.2,0],0,.1,310,240,112),L=norm([-.45,.6,-.65]),Hh=norm(add(L,[0,0,-1])),center=[0,.2,13],fy=-2.8,tilt=rotator(.42+.08*Math.sin(t*.1),0,.18),real=[],refl=[];
 orbSpec.forEach((o,i)=>{const a=i/orbSpec.length*Math.PI*2+t*.13,p=add(tilt([Math.cos(a)*5.6,Math.sin(t*.7+i)*.18,Math.sin(a)*5.6]),center);for(const tr of orbMesh){const cs=tr.n.map(n=>orbShade(o,n,p,o.r,cam,L,Hh)),pts=tr.p.map(v=>add(p,mul(v,o.r)));real.push({p:pts,c:cs});refl.push({p:pts.map(q=>[q[0],2*fy-q[1],q[2]]),c:cs.map(c=>mix(c,[170,180,220],.45))});}});
 const fog={fog:flo,fogNear:12,fogRange:30};pTris(cam,refl,fog);pTris(cam,[{p:[[-40,fy,2],[40,fy,60],[40,fy,2]],c:[flo,flo,flo],a:.55},{p:[[-40,fy,2],[-40,fy,60],[40,fy,60]],c:[flo,flo,flo],a:.55}],fog);pTris(cam,real,fog);}

// ---------- PSP ----------
function pspField(T,t){const {W,H}=T,h=pspHue(t),cam=camera([0,1.4,0],0,.06,W*.62,W/2,H*.44),Lt=norm([0,.6,.8]);gClear(T,[[0,hsl(h,.55,.16)],[.45,hsl(h,.55,.32)],[1,hsl(h,.55,.32)]]);
 const hf=(x,z)=>.55*Math.sin(x*.25+z*.12-t*.5)+.3*Math.sin(-x*.18+z*.3-t*.7);
 gTris(T,cam,gridTris(surfRows(hf,zsGeom(1.2,45,1.14),40,4,1),(a,b,c)=>({p:[a.p,b.p,c.p],c:[a,b,c].map(v=>{const ht=clamp((v.p[1]+.9)/1.8,0,1);return mix(hsl(h+ht*.06,.6,.3+ht*.3),[255,255,255],Math.pow(Math.max(0,dot(v.n,Lt)),12)*.6);})})),{fog:hsl(h,.55,.3),near:10,range:35});}

// ---------- PS3 ----------
function ps3Sheet(T,t){const {W,H}=T,cam=camera([0,1.4,0],0,.08,W*.62,W/2,H*.43);ps3Bg(T);
 for(let zi=0;zi<40;zi++){const z=2+zi*.7;for(let xi=-30;xi<=30;xi++){const x=xi*.45,y=-1.4+.6*Math.sin(x*.35+t*.8)*Math.cos(z*.22-t*.5)+.25*Math.sin(z*.5+x*.2-t),p=cam.view([x,y,z]);if(p[2]<.5)continue;const k=clamp((y+2)/1.4,0,1)*clamp(1.3-z/30,0,1);gPix(T,cam.cx+p[0]*cam.f/p[2],cam.cy-p[1]*cam.f/p[2],[120+k*120,160+k*80,255],.35+k*.6,true);}}}

// ---------- Arcade ----------
function arcVector2(T,t){const {W,H}=T,cam=camera([0,1.6,0],0,.1,W*.68,W/2,H*.43),g=[60,255,140],tr=mod(t*2,2),seg=(p,q,a)=>{const A=cam.view(p),B=cam.view(q);gSeg(T,cam.cx+A[0]*cam.f/A[2],cam.cy-A[1]*cam.f/A[2],cam.cx+B[0]*cam.f/B[2],cam.cy-B[1]*cam.f/B[2],g,.8,a,true);};arcVector(T,t);
 for(let k=-10;k<=10;k++)seg([k*2,-1.6,2],[k*2,-1.6,40],.45);for(let k=0;k<20;k++){const z=2+k*2-tr;if(z>=2)seg([-20,-1.6,z],[20,-1.6,z],.45*clamp(1.4-z/30,0,1));}}
function arcBulbs(T,t){const {W,H}=T,sp=10;gClear(T,[[0,[30,4,10]],[1,[70,14,20]]]);
 for(let gy=sp/2;gy<H;gy+=sp)for(let gx=sp/2;gx<W;gx+=sp){const d=Math.hypot(gx-W/2,(gy-H*.43)*1.5),edge=gx<sp||gx>W-sp||gy<sp||gy>H-sp,a=Math.atan2(gy-H*.43,gx-W/2),on=edge?(Math.floor(gx/sp)+Math.floor(gy/sp)+Math.floor(t*8))%3===0:(.5+.5*Math.sin(d*.09-t*3))*(.5+.5*Math.sin(a*3+t*1.5))>.55&&d>45;
  if(on){gGlow(T,gx,gy,3.2,[255,170,80],.45);gDisc(T,gx,gy,2,[255,240,200],1,1);}else gDisc(T,gx,gy,1.8,[110,50,30],1,1);}}
function arcChecker(T,t){const {W,H}=T;gClear(T,[[0,[40,90,220]],[.42,[250,170,200]],[1,[250,170,200]]]);
 gPlane(T,{hz:H*.42,hgt:1.2,f:W*.6,x0:Math.sin(t*.4)*3,z0:t*5,fog:[250,190,210],near:10,range:40,pat:(X,Z,d,o)=>{const c=(Math.floor(X/1.6)+Math.floor(Z/1.6))&1?[60,200,90]:[240,240,220];o[0]=c[0];o[1]=c[1];o[2]=c[2];}});}
function arcPlating(T,t){const {W,H}=T,S=32;for(let y=0;y<H;y++)for(let x=0;x<W;x++){const Y=y-t*14,u=mod(x,S),v=mod(Y,S),h=hash(Math.floor(x/S)*7.3+Math.floor(Y/S)*3.1);let col=mix([46,52,66],[70,78,96],h*.6);
 if(u<1||v<1)col=[96,104,124];else if(u>S-2||v>S-2)col=[24,26,36];else if((u===4||u===S-5)&&(v===4||v===S-5))col=[150,156,172];else if(h>.82&&u>8&&u<S-8&&v>8&&v<S-8)col=mix([20,24,30],[80,220,255],(.5+.5*Math.sin(t*3+h*20))*((Math.floor(v)&3)<2?1:.4));else if(h<.15&&u>6&&u<S-6&&((Math.floor(u)+Math.floor(v))&7)<2)col=[34,38,50];
 if(Math.abs(x-W/2)<34&&Math.abs(y-H*.43)<30)col=mix(col,[30,32,44],.4);gSet(T,x,y,col);}}

// ---------- Steam ----------
function steamWall2(T,t){const {W,H}=T,fog=ST.dk,cam=camera([0,0,0],0,0,W*.68,W/2,H*.43),real=[],refl=[],fy=-2.9,o={fog,near:6,range:22};gClear(T,[[0,ST.dk],[1,ST.sl]]);
 for(const side of [-1,1])for(let r=0;r<4;r++)for(let k=0;k<14;k++){const z=mod(k*1.6-t*.7*side,22.4)+3,x=side*4.6,y=fy+1.05+r*2.1,h=1.75,w=1.2,col=hsl(.55+hash(k*7+r*3+side)*.12,.35,.28+hash(k+r+side*.5)*.18),top=mix(col,[220,235,250],.25),P=[[x,y-h/2,z-w/2],[x,y-h/2,z+w/2],[x,y+h/2,z+w/2],[x,y+h/2,z-w/2]],M=P.map(p=>[p[0],2*fy-p[1],p[2]]),dc=mix(col,ST.dk,.5),dt=mix(top,ST.dk,.5);
  real.push({p:[P[0],P[1],P[2]],c:[col,col,top]},{p:[P[0],P[2],P[3]],c:[col,top,top]});refl.push({p:[M[0],M[1],M[2]],c:[dc,dc,dt],a:.5},{p:[M[0],M[2],M[3]],c:[dc,dt,dt],a:.5});}
 gTris(T,cam,refl,o);gTris(T,cam,[{p:[[-30,fy,2],[30,fy,40],[30,fy,2]],col:[30,40,56],a:.55},{p:[[-30,fy,2],[-30,fy,40],[30,fy,40]],col:[30,40,56],a:.55}],o);gTris(T,cam,real,o);}
function steamMesh(T,t){const {W,H}=T,cam=camera([0,2.4,0],0,.2,W*.62,W/2,H*.43),L=norm([-.4,.7,-.3]),travel=t*.9;gClear(T,[[0,ST.dk],[1,ST.sl]]);
 const hf=(x,z)=>{const Z=z+travel;return .7*Math.sin(x*.3+Z*.2)+.5*Math.sin(-x*.22+Z*.37)+Math.min(1.2,Math.abs(x)*.12)-.4;};
 gTris(T,cam,gridTris(surfRows(hf,zsGeom(1.6,40,1.18),22,4,.9),(a,b,c)=>({p:[a.p,b.p,c.p],col:mix([24,36,54],[88,160,214],Math.pow(clamp(dot(faceN(a,b,c),L),0,1),3)*.6)})),{fog:ST.dk,near:8,range:30});}
function steamGrid(T,t){const {W,H}=T;gClear(T,[[0,ST.dk],[1,ST.sl]]);
 gPlane(T,{hz:H*.3,hgt:1.5,f:W*.6,z0:t*.35,yaw:Math.sin(t*.05)*.08,fog:ST.dk,near:5,range:26,pat:(X,Z,d,o)=>{const id=hash(Math.floor(X/1.4)*7.7+Math.floor(Z/2)*3.1),p=Math.pow(Math.max(0,Math.sin(t*.7+id*20)),8),c=mod(X,1.4)>.12&&mod(Z,2)>.12?mix(hsl(.56+id*.06,.35,.2+id*.1),[150,210,250],p*.8):[16,20,28];o[0]=c[0];o[1]=c[1];o[2]=c[2];}});}

// =====================================================================
// Round 6 (after the second rating pass): each Maybe gets an improved
// version shown next to the current one for comparison; Don'ts are
// dropped, except on 3DS, PSP and PS3, which get two new ideas each in
// the one direction the owner liked there.
// =====================================================================
function snesLake2(T,t){const {W,H}=T,hz=Math.floor(H*.42),sky=[[198,191,225],[208,198,228],[218,206,230],[228,214,232],[236,222,232],[242,230,231],[244,234,229]],wc=[[150,164,204],[166,176,209],[189,186,222],[208,206,232],[226,224,238]];
 for(let y=0;y<=hz;y++){const c=sky[Math.min(6,Math.floor(y/hz*7))];for(let x=0;x<W;x++)gSet(T,x,y,c);}
 gPlane(T,{hz,hgt:1,f:175,yaw:.22*Math.sin(t*.07),z0:t*.9,fog:[244,234,229],near:6,range:55,wob:y=>Math.sin(y*.55+t*2.4)*(y-hz)*.025,pat:(X,Z,d,o)=>{const w=Math.sin(X*.7+Math.sin(Z*.4+t*.6)*1.4+t*.5)*.6+Math.sin(Z*1.1-t*.8)*.4,q=clamp(Math.floor((w+1)*2.5),0,4),r=wc[q];o[0]=r[0];o[1]=r[1];o[2]=r[2];}});}
function gbaVoxel2(T,t){const {W,H,c}=T,hz=H*.36,camZ=t*6,camX=Math.sin(t*.08)*30,camH=24,yb=new Float32Array(W).fill(H),sky=[236,226,246],levels=[GV.teal,mix(GV.teal,GV.lav,.5),GV.lav,mix(GV.lav,GV.pink,.5),GV.pink,GV.cream];
 for(let y=0;y<hz+1;y++){const k=y/hz,band=Math.floor(k*6)/6,col=mix([132,120,204],sky,k*.5+band*.5>gBayer[(y&3)*4]/16*.2+.4?band:band);for(let x=0;x<W;x++){const j=(y*W+x)*3;c[j]=col[0];c[j+1]=col[1];c[j+2]=col[2];}}
 for(let y=Math.floor(hz)+1;y<H;y++)for(let x=0;x<W;x++){const j=(y*W+x)*3;c[j]=sky[0];c[j+1]=sky[1];c[j+2]=sky[2];}
 const hgt=(X,Z)=>9*Math.sin(X*.045)*Math.cos(Z*.035)+6*Math.sin(X*.1+Z*.07)+4*Math.cos(Z*.12-X*.03)+Math.min(18,Math.abs(X-camX)*.22);
 for(let z=3,dz=.8;z<140;z+=dz,dz*=1.03){const fog=clamp((z-25)/100,0,1);for(let x=0;x<W;x+=2){const X=camX+(x+1-W/2)*z/(W*.55),hr=hgt(X,camZ+z),lv=clamp(Math.floor(hr/5),0,5),h=lv*5,sy=Math.round(hz+(camH-h)*H*.9/z);if(sy<yb[x]){const col=mix(levels[lv],sky,Math.floor(fog*4)/4);for(let y=Math.max(0,sy);y<Math.min(H,Math.ceil(yb[x]));y++)for(let k=0;k<2&&x+k<W;k++){const j=(y*W+x+k)*3;c[j]=col[0];c[j+1]=col[1];c[j+2]=col[2];}yb[x]=sy;if(x+1<W)yb[x+1]=sy;}}}}
function gbaCorridor2(T,t){const {W,H,c}=T,f=W*.55,hz=H*.43,camZ=t*1.6,fog=[206,198,238],wall=(u,v,side)=>{const m=mod(u,2),seam=m<.08||v<.05||v>.95,win=hash(Math.floor(u/2)*3.1+side)>.55&&v>.28&&v<.64&&m>.35&&m<1.65;return seam?GV.vio:win?mix(GV.teal,[220,250,250],.3):v<.5?GV.cream:mix(GV.cream,GV.lav,.5);};
 for(let x=0;x<W;x++){const dx=(x+.5-W/2)/f,zw=2/Math.max(1e-3,Math.abs(dx)),top=hz-1.3*f/zw,bot=hz+f/zw,side=dx<0?0:1,u=camZ+zw;
  for(let y=0;y<H;y++){let z,col;if(y+.5<top){z=1.3*f/(hz-y-.5);col=mod(camZ+z,3)<.5&&Math.abs(dx*z)<1.2?[255,250,240]:GV.lav;}
   else if(y+.5>bot){z=f/(y+.5-hz);const ck=(Math.floor(dx*z)+Math.floor(camZ+z))&1;col=ck?GV.lav:GV.cream;const vr=1-(y+.5-bot)/(bot-top);if(vr>0)col=mix(col,wall(u,vr,side),.28*vr);}
   else{z=zw;col=wall(u,(y+.5-top)/(bot-top),side);}
   col=mix(col,fog,clamp((z-3)/24,0,1));const j=(y*W+x)*3;c[j]=col[0];c[j+1]=col[1];c[j+2]=col[2];}}}
function gbaTwist2(T,t){const {W,H,c}=T,hz=H*.42,f=W*.55;
 for(let y=0;y<H;y++){const ceil=y+.5<hz,dy=Math.abs(y+.5-hz);if(dy<.5){for(let x=0;x<W;x++){const j=(y*W+x)*3;c[j]=GV.cream[0];c[j+1]=GV.cream[1];c[j+2]=GV.cream[2];}continue;}
  const dist=(ceil?1.4:1)*f/dy,yaw=t*.07+Math.sin(t*.22)*dist*.05*(ceil?-1:1),cy=Math.cos(yaw),sy=Math.sin(yaw),fog=clamp((dist-4)/24,0,1);
  for(let x=0;x<W;x++){const lx=(x+.5-W/2)*dist/f,X=lx*cy+dist*sy,Z=-lx*sy+dist*cy+t*1.1,u=mod(X,2),v=mod(Z,2),e=Math.min(u,v,2-u,2-v),top=e<.1?GV.vio:e<.22?mix(GV.lav,GV.cream,.7):(u+v<1.2?mix(GV.lav,GV.cream,.45):GV.lav),col=mix(ceil?mix(top,GV.vio,.25):top,GV.cream,fog),j=(y*W+x)*3;c[j]=col[0];c[j+1]=col[1];c[j+2]=col[2];}}}
function gcField2(T,t){const {W,H}=T,cam=camera([Math.sin(t*.08)*1.5,3.2,-1],Math.sin(t*.08)*-.05,.3,W*.62,W/2,H*.4),L=norm([Math.cos(t*.3)*.5,.7,-.4]),tris=[];gClear(T,[[0,GCC.deep],[1,GCC.ind]]);
 for(let j=0;j<16;j++)for(let i=-10;i<=10;i++){const x=i*.9,z=2.5+j*.9,w=.5+.5*Math.sin(Math.hypot(x,z-10)*.55-t*1.2),h=.15+.9*w*w,hot=hash(i*13+j*7)>.97;tris.push(...place(boxMesh,ident,[.4,h/2,.4],[x,h/2-1,z],{m:{base:hot?GCC.amb:mix(GCC.ind,GCC.lil,.25+w*.55),spec:1,shin:50,rim:.5,rimCol:[200,190,255]}}));}
 gTris(T,cam,tris,{amb:.28,L,fog:GCC.deep,near:5,range:13});}
function dsRippleFloor2(t){dFill([240,243,247],[226,232,240]);const cam=camera([0,2.4,0],0,.2,200,128,58),taps=[];
 for(let k=Math.floor((t-5)/.8);k<=Math.floor(t/.8);k++){const tk=k*.8;if(tk>t||t-tk>=5)continue;taps.push([(hash(k)-.5)*14,5+hash(k+.5)*14,t-tk]);}
 const h=(x,z)=>{let s=0;for(const [tx,tz,age] of taps){const w=Math.hypot(x-tx,z-tz)-age*2.6;if(w<0&&w>-3)s+=Math.sin(w*3)*.5*(1-age/5)*(1+w/3);}return s;};
 const plot=(x,z)=>{const y=h(x,z),p=cam.view([x,y,z]);if(p[2]<.5)return;const fogk=Math.floor(clamp((z-3)/26,0,1)*8)/8;dPlot(cam.cx+p[0]*cam.f/p[2],cam.cy-p[1]*cam.f/p[2],mix(y>.12?ICE:y<-.12?NAVY:[150,162,188],[240,243,247],fogk*.85),1);};
 for(let xi=-24;xi<=24;xi++)for(let z=2;z<30;z+=.1)plot(xi*.75,z);for(let zi=0;zi<38;zi++)for(let x=-22;x<=22;x+=.08)plot(x,2+zi*.75);
 for(const [tx,tz,age] of taps)if(age<.35){const p=cam.view([tx,0,tz]);if(p[2]>.5){const sx=cam.cx+p[0]*cam.f/p[2],sy=cam.cy-p[1]*cam.f/p[2];for(let dx=-2;dx<=2;dx++)for(let dy=-1;dy<=1;dy++)dPlot(sx+dx,sy+dy,NAVY,1-age/.35);}}}
function wiiStripes2(T,t){const {W,H,c}=T,s=W/480;
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){const X=x/s,Y=y/s,a=X*.009+t*.35,b=Y*.008-t*.2,w=Math.sin(a)*Math.sin(b)+.4*Math.sin(X*.004+Y*.006-t*.15),slope=(Math.cos(a)*.009*Math.sin(b)+.4*.004*Math.cos(X*.004+Y*.006-t*.15))*22,stripe=mod(Y+w*22,6)<1.3,shade=clamp(1+slope*1.6,.86,1.08);
  let col=mix([252,253,254],[228,233,238],Y/(H/s));col=mix(col,[214,236,248],clamp(w,0,1)*.35);if(stripe)col=mix(col,[196,206,218],.5);col=mul(col,shade);const j=(y*W+x)*3;c[j]=col[0];c[j+1]=col[1];c[j+2]=col[2];}}
function mdSpeed3(T,t){const {W,H}=T,hz=Math.floor(H*.47),bands=[MD.navy,[0,0,109],MD.dk,[36,36,145],MD.blue,[72,109,218],MD.cyan];
 for(let y=0;y<=hz;y++){const k=y/hz*6.99,b=Math.floor(k);for(let x=0;x<W;x++)gSet(T,x,y,bands[k-b>gBayer[(y&3)*4+(x&3)]/16?Math.min(6,b+1):b]);}
 for(let x=0;x<W;x++)gSet(T,x,hz,MD.white);
 for(let y=hz+1;y<H;y++){const z=500/(y-hz),stripe=Math.floor(z*.2+t*14)&1,curve=Math.sin(t*.4)*60/(1+(y-hz)*.1);for(let x=0;x<W;x++){const ck=(Math.floor((x-W/2+curve)*z/100)&1)^stripe;let col=ck?(stripe?MD.cyan:MD.blue):(stripe?MD.blue:MD.dk);if(z>35&&(x+y)&1)col=MD.blue;gSet(T,x,y,col);}}
 for(let i=0;i<22;i++){const y=Math.floor(hz*.15+hash(i)*hz*.75),v=160+hash(i+.3)*260,len=v*.15,head=mod(hash(i+.6)*(W+200)-t*v,W+200)-60;for(let k=0;k<len;k++){const x=Math.floor(head+k);if(x<0||x>=W)continue;gPix(T,x,y,k<len*.2?MD.white:MD.cyan,1-k/len);}}}
function mdPipe2(T,t){const {W,H}=T,f=W*.55,cy=H*.43;
 for(let y=0;y<H;y++)for(let x=0;x<W;x++){const dx=(x+.5-W/2)/f,dy=-(y+.5-cy)/f,z=2.2/Math.max(.02,Math.hypot(dx,dy)),ang=Math.atan2(dy,dx)+Math.sin(z*.08-t*.3)*.4,q=ang*8/Math.PI,ck=(Math.floor(q)+Math.floor(z*.5+t*6))&1,upper=dy>0;
  let col=mod(q,1)<.08?MD.white:ck?(upper?MD.blue:MD.cyan):(upper?MD.dk:MD.blue);if(z>26&&(x+y)&1)col=MD.navy;if(z>40)col=MD.navy;gSet(T,x,y,col);}}
function dcFog3(T,t){const {W,H}=T,fog=[253,238,224],cam=camera([Math.sin(t*.07)*1.2,2.4,0],Math.sin(t*.07)*-.04,.16,W*.62,W/2,H*.43),L=norm([.5,.8,-.3]),tris=[];gClear(T,[[0,[252,222,198]],[.42,fog],[1,fog]]);
 for(let j=0;j<14;j++)for(let i=-8;i<=8;i++){const z=mod(j*1.3-t*1.1,18.2)+3,w=.5+.5*Math.sin(i*.5+z*.45-t*.6),h=.2+1.1*w*w,hot=hash(i*5+j*3)>.88;tris.push(...place(boxMesh,ident,[.5,h/2,.5],[i*1.3,h/2-1,z],{m:{base:hot?DC.or:mix([250,250,248],[235,240,250],w),spec:.3,shin:20,rim:.1}}));}
 gTris(T,cam,tris,{fog,near:5,range:15,amb:.62,L});}
function dcSea2(T,t){const {W,H}=T,cam=camera([0,1.7,0],0,.07,W*.62,W/2,H*.42),L=norm([.25,.65,.7]),Hh=norm(add(L,[0,.25,-1]));gClear(T,[[0,[30,110,220]],[.42,[160,215,250]],[1,[160,215,250]]]);
 const hf=(x,z)=>.28*Math.sin(x*.55+z*.4-t*1)+.16*Math.sin(-x*.4+z*.85-t*1.4)+.06*Math.sin(x*1.6+z*1.3-t*2.2);
 gTris(T,cam,gridTris(surfRows(hf,zsGeom(1.2,50,1.12),48,4,1),(a,b,c)=>({p:[a.p,b.p,c.p],c:[a,b,c].map(v=>{const d=Math.max(0,dot(v.n,L)),sp=Math.pow(Math.max(0,dot(v.n,Hh)),60);return [16+d*40+sp*240,140+d*60+sp*115,200+d*40+sp*55];})})),{fog:[176,222,250],near:12,range:40});}
function dcChecker2(T,t){const {W,H}=T,hz=H*.4,sky=[[40,120,225],[180,222,250]];gClear(T,[[0,sky[0]],[.4,sky[1]],[1,sky[1]]]);
 gPlane(T,{hz,hgt:1.5,f:W*.6,yaw:t*.035,z0:t*.8,fog:[244,236,228],near:10,range:40,pat:(X,Z,d,o)=>{const c=(Math.floor(X/1.6)+Math.floor(Z/1.6))&1?[246,170,120]:[252,246,238];o[0]=c[0];o[1]=c[1];o[2]=c[2];}});
 for(let y=Math.floor(hz)+1;y<H;y++){const ry=2*hz-y,k=.32*Math.exp(-(y-hz)/(H*.18)),sc=mix(sky[0],sky[1],clamp(ry/hz,0,1));for(let x=0;x<W;x++)gBlend(T.c,(y*W+x)*3,sc,k,false);}
 for(let y=Math.floor(hz)-3;y<hz+4;y++)for(let x=0;x<W;x++)gBlend(T.c,(y*W+x)*3,[255,255,255],.35*Math.exp(-((y-hz)**2)/4),false);}
function ps1Terrace2(T,t){const {W,H}=T,fog=[233,233,222],cam=camera([Math.sin(t*.1)*.6,3.4,-1],Math.sin(t*.08)*.05,.32,W*.55,W/2,H*.4),tris=[];gClear(T,[[0,[188,204,213]],[.5,fog],[1,fog]]);
 for(let j=0;j<13;j++)for(let i=-9;i<=9;i++){const x=i*1.2,z=2+j*1.2,raw=1.4+1.1*Math.sin(i*.45+t*.18)+.9*Math.cos(j*.5-t*.15),h=Math.abs(i)<=1&&j<4?.05:Math.max(.05,Math.round(raw*4)/4*.35),y0=-1,y1=y0+h,s=.6,a=[x-s,y1,z-s],b=[x-s,y1,z+s],c=[x+s,y1,z+s],d=[x+s,y1,z-s],sx=x<0?x+s:x-s;
  texQuad(tris,a,b,c,d,PST[mod(i*3+j,6)],1);texQuad(tris,[x-s,y0,z-s],a,d,[x+s,y0,z-s],PST[mod(i+j*2,6)],.72);texQuad(tris,[sx,y0,z-s],[sx,y1,z-s],[sx,y1,z+s],[sx,y0,z+s],PST[mod(i+j,6)],.82);}
 gTris(T,cam,tris,{fog,near:5,range:16,snap:true});}
function ps1Pipe2(T,t){const {W,H}=T,fog=[236,236,228],cam=camera([0,0,0],0,0,W*.55,W/2,H*.43),tris=[],R=3.6,N=10,seg=2.2,travel=mod(t*1.3,seg),row0=Math.floor(t*1.3/seg);gClear(T,[[0,fog],[1,fog]]);
 const ctr=z=>[Math.sin(z*.05+t*.12)*1.2,Math.cos(z*.04+t*.1)*.6],P=(c,a,z)=>[c[0]+Math.cos(a)*R*1.55,c[1]+Math.sin(a)*R,z];
 for(let k=0;k<12;k++){const z0=1+k*seg-travel,z1=z0+seg,c0=ctr(z0+t*1.3),c1=ctr(z1+t*1.3),rid=k+row0;for(let i=0;i<N;i++){const a0=i/N*Math.PI*2,a1=(i+1)/N*Math.PI*2;texQuad(tris,P(c0,a0,z0),P(c1,a0,z1),P(c1,a1,z1),P(c0,a1,z0),PST[mod(rid%3===0?4:i%2?1:3,6)],.82+.18*Math.cos(a0-1.2));}}
 gTris(T,cam,tris,{fog,near:5,range:22,snap:true});}
const orbBig=Array.from({length:7},(_,i)=>({r:.85+(i%3)*.18,metal:[.75,.4,.85,.3,.6,.5,.8][i],base:[[120,140,220],[214,218,236],[96,110,200],[180,196,240],[150,160,230],[200,206,240],[110,124,210]][i]}));
function ps2Orbs3(t){const flo=[206,212,238];pSky([186,196,234],[224,228,246],PH*.5);const cam=camera([0,2.4,0],0,.2,310,240,108),L=norm([-.45,.6,-.65]),Hh=norm(add(L,[0,0,-1])),fy=-1.6,real=[],refl=[],floor=[];
 for(let zi=0;zi<16;zi++)for(let xi=-10;xi<10;xi++){const x=xi*2,z=3+zi*2,c=((xi+zi)&1)?flo:mul(flo,.95);floor.push({p:[[x,fy,z],[x+2,fy,z+2],[x+2,fy,z]],c:[c,c,c],a:.62},{p:[[x,fy,z],[x,fy,z+2],[x+2,fy,z+2]],c:[c,c,c],a:.62});}
 orbBig.forEach((o,i)=>{const a=i/orbBig.length*Math.PI*2+t*.11,p=[Math.cos(a)*5.2,fy+o.r,13+Math.sin(a)*3.4],spin=rotator(0,0,-t*.11*5.2/o.r);for(const tr of orbMesh){const ns=tr.n.map(spin),cs=ns.map(n=>orbShade(o,n,p,o.r,cam,L,Hh)),pts=tr.p.map(v=>add(p,mul(v,o.r)));real.push({p:pts,c:cs});refl.push({p:pts.map(q=>[q[0],2*fy-q[1],q[2]]),c:cs.map(c=>mix(c,flo,.5))});}});
 const fog={fog:[224,228,246],fogNear:12,fogRange:30};pTris(cam,refl,fog);pTris(cam,floor,fog);pTris(cam,real,fog);}
function pspField2(T,t){const {W,H}=T,h=pspHue(t),cam=camera([0,1.4,0],0,.06,W*.62,W/2,H*.44),Lt=norm([0,.6,.8]);gClear(T,[[0,hsl(h,.6,.1)],[.45,hsl(h,.55,.28)],[1,hsl(h,.55,.28)]]);
 const hf=(x,z)=>.55*Math.sin(x*.25+z*.12-t*.5)+.3*Math.sin(-x*.18+z*.3-t*.7);
 gTris(T,cam,gridTris(surfRows(hf,zsGeom(1.2,45,1.12),44,4,1),(a,b,c)=>({p:[a.p,b.p,c.p],c:[a,b,c].map(v=>{const ht=clamp((v.p[1]+.9)/1.8,0,1);return mix(hsl(h+ht*.05,.6,.18+ht*.3),[255,255,255],Math.pow(Math.max(0,dot(v.n,Lt)),14)*.55);})})),{fog:hsl(h,.55,.26),near:10,range:32});
 for(const z0 of [3.2,5.5,9]){const pts=[];for(let x=-12;x<=12;x+=.3){const p=cam.view([x,hf(x,z0)+.02,z0]);if(p[2]>.4)pts.push([cam.cx+p[0]*cam.f/p[2],cam.cy-p[1]*cam.f/p[2]]);}gStroke(T,pts,[255,255,255],1.1,.35,true);gStroke(T,pts,hsl(h,.6,.7),4,.08,true);}}
function arcVector3(T,t){const {W,H}=T,cam=camera([0,2.2,0],0,.14,W*.68,W/2,H*.4),hf=(x,z)=>{const Z=z+t*2.5;return -1.6+(Math.abs(x)>3.5?(.9*Math.sin(x*.4+Z*.3)+.6*Math.sin(Z*.21-x*.2)+1.2)*clamp((Math.abs(x)-3.5)/4,0,1):0);},P=(x,z)=>{const q=cam.view([x,hf(x,z),z]);return q[2]>.3?[cam.cx+q[0]*cam.f/q[2],cam.cy-q[1]*cam.f/q[2]]:null;},g=[80,255,150];gClear(T,[[0,[2,4,6]],[1,[3,8,8]]]);
 for(let xi=-12;xi<=12;xi++){const pts=[];for(let z=2;z<36;z+=.8){const p=P(xi*1.2,z);if(p)pts.push(p);}gStroke(T,pts,g,.8,.45*(1-Math.abs(xi)/16),true);}
 const off=mod(t*2.5,1.6);for(let k=0;k<22;k++){const z=2+k*1.6-off;if(z<2)continue;const pts=[];for(let x=-14.4;x<=14.4;x+=.6){const p=P(x,z);if(p)pts.push(p);}gStroke(T,pts,g,.8,.5*clamp(1.3-z/34,0,1),true);}
 const S=AR_OCT,rot=rotator(t*.5,t*.7,0),pos=[0,2.6,14],V=S.v.map(v=>{const q=cam.view(add(rot(v),pos));return [cam.cx+q[0]*cam.f/q[2],cam.cy-q[1]*cam.f/q[2]];});for(const [a,b] of S.e)gSeg(T,V[a][0],V[a][1],V[b][0],V[b][1],[90,220,255],.9,.8,true);}
function arcBulbs2(T,t){const {W,H}=T,sp=14;gClear(T,[[0,[26,4,8]],[1,[60,12,18]]]);
 for(let gy=sp/2;gy<H;gy+=sp)for(let gx=sp/2;gx<W;gx+=sp){const d=Math.hypot(gx-W/2,(gy-H*.43)*1.5),edge=gx<sp||gx>W-sp||gy<sp||gy>H-sp,ring=.5+.5*Math.sin(d*.07-t*1.6),tw=hash(Math.floor(gx/sp)*7.1+Math.floor(gy/sp)*3.3+Math.floor(t*2)*.17)>.96,on=edge?(Math.floor(gx/sp)+Math.floor(gy/sp)+Math.floor(t*5))%4===0:(d>50&&(ring>.8||tw));
  const lvl=edge?1:ring;if(on){gGlow(T,gx,gy,4.2,[255,165,80],.35*lvl+.15);gDisc(T,gx,gy,2.6,[255,238,196],1,1);}else gDisc(T,gx,gy,2.4,mix([84,34,24],[150,80,40],ring*.4),1,1);}}
function steamGrid2(T,t){const {W,H}=T;gClear(T,[[0,ST.dk],[1,ST.sl]]);
 gPlane(T,{hz:H*.3,hgt:1.5,f:W*.6,z0:t*.3,yaw:Math.sin(t*.05)*.08,fog:ST.dk,near:5,range:24,pat:(X,Z,d,o)=>{const u=mod(X,1.3),v=mod(Z,1.9),cx=Math.floor(X/1.3),cz=Math.floor(Z/1.9),id=hash(cx*7.7+cz*3.1);if(u<.1||v<.1){o[0]=14;o[1]=18;o[2]=26;return;}
  const hue=.5+id*.18,diag=(u/1.3+v/1.9)/2,art=mix(hsl(hue,.35,.22),hsl(hue+.06,.45,.42),diag>.5?1:diag*2),glow=Math.pow(Math.max(0,Math.sin(t*.5+id*20)),10)*.5,sweep=Math.exp(-((mod(X*.5+Z-t*2.5,30)-15)**2)/6)*.25,c=mix(art,[180,220,250],glow+sweep);o[0]=c[0];o[1]=c[1];o[2]=c[2];}});}
// ---------- new: 3DS (tile-wave family) ----------
function n3FloorWave(t){fBg(t,[224,238,242],[246,248,249]);const cam=swayCam(t,.9,10,2.6,260,200,96,.28),L=movingLight(t*.7),tris=[],rx=rotator(Math.PI/2,0,0);
 for(let j=0;j<12;j++)for(let i=-8;i<=8;i++){const x=i*1.2,z=4+j*1.2,dist=Math.hypot(x,z-9),wave=Math.pow(Math.max(0,Math.sin(dist*.6-t*1.2)),3),hot=((i*7+j*13)&15)===3;tris.push(...place(pillow,rx,[.56,.56,.14],[x,-1.6+wave*.9,z],{m:{base:hot?CORAL:mix(PEARL,AQUA_L,.25+wave*.75),shin:46,rim:.45}}));}
 fTris(cam,tris,L);}
function n3TapWave(t){fBg(t,[222,236,240],[244,247,249]);const cam=swayCam(t,1.1,11,0,260,200,104),L=movingLight(t*.8),tris=[],taps=[];
 for(let k=Math.floor((t-4)/1.3);k<=Math.floor(t/1.3);k++){const tk=k*1.3;if(tk>t||t-tk>=4)continue;const a=hash(k)*Math.PI*2;taps.push([Math.cos(a)*(4+hash(k+.5)*4),Math.sin(a)*(2+hash(k+.7)*2.4),t-tk]);}
 for(let j=-3;j<=3;j++)for(let i=-6;i<=6;i++){if(Math.abs(i)<=1&&Math.abs(j)<=1)continue;const x=i*1.62,y=j*1.62;let pop=0;for(const [tx,ty,age] of taps){const w=Math.hypot(x-tx,y-ty)-age*5;if(w<0&&w>-2.4)pop+=Math.sin(-w/2.4*Math.PI)*(1-age/4);}pop=clamp(pop,0,1);
  tris.push(...place(pillow,rotator(0,0,0),[.56,.56,.14],[x,y,11.5-pop*1.6],{m:{base:mix(PEARL,AQUA_L,.25+pop*.75),shin:46,rim:.45}}));}
 fTris(cam,tris,L);}
// ---------- new: PSP (theme-wave family) ----------
function pspRibbon(T,t){const {W,H}=T,h=pspHue(t),cam=camera([0,0,0],0,0,W*.62,W/2,H*.44);gClear(T,[[0,hsl(h,.6,.12)],[1,hsl(h,.55,.34)]]);
 for(let r=0;r<2;r++){const tris=[],edge=[[],[]];for(let i=0;i<=60;i++){const x=-14+i*28/60,base=r?-1.6:-2.6,y=base+Math.sin(x*.22+t*(.4+r*.1)+r*2)*1.1,z=12+Math.sin(x*.15-t*.3+r)*2,tw=Math.sin(x*.18+t*.5+r*1.3)*.9,a=[x,y-tw*.5,z-tw],b=[x,y+tw*.5+.25,z+tw];edge[0].push(a);edge[1].push(b);}
  for(let i=0;i<60;i++){const a=edge[0][i],b=edge[1][i],c=edge[1][i+1],d=edge[0][i+1],c1=hsl(h+.03,.5,.75),c2=hsl(h,.55,.55);tris.push({p:[a,b,c],c:[c2,c1,c1],a:.32},{p:[a,c,d],c:[c2,c1,c2],a:.32});}gTris(T,cam,tris);
  for(const E of edge){const pts=E.map(p=>{const q=cam.view(p);return [cam.cx+q[0]*cam.f/q[2],cam.cy-q[1]*cam.f/q[2]];});gStroke(T,pts,[255,255,255],1,.4,true);}}}
function pspTiles(T,t){const {W,H}=T,h=pspHue(t),hc=hsl(h,.6,.6);gClear(T,[[0,[8,8,18]],[.32,hsl(h,.5,.2)],[1,[6,6,14]]]);
 gPlane(T,{hz:H*.32,hgt:1.4,f:W*.6,z0:t*.35,fog:hsl(h,.4,.14),near:6,range:22,pat:(X,Z,d,o)=>{const u=mod(X,1.4),v=mod(Z,1.4),e=Math.min(u,v,1.4-u,1.4-v),wave=Math.pow(Math.max(0,Math.sin(Math.hypot(Math.floor(X/1.4),Math.floor(Z/1.4)-8)*.5-t*1.2)),4),c=e<.06?mix([30,32,50],hc,wave):mix([12,13,24],mix([12,13,24],hc,.25),wave*clamp(1-e*3,0,1));o[0]=c[0];o[1]=c[1];o[2]=c[2];}});}
// ---------- new: PS3 (particle-sheet family) ----------
function ps3Wire(T,t){const {W,H}=T,cam=camera([0,1.4,0],0,.08,W*.62,W/2,H*.43),hf=(x,z)=>-1.4+.6*Math.sin(x*.35+t*.8)*Math.cos(z*.22-t*.5)+.25*Math.sin(z*.5+x*.2-t),P=(x,z)=>{const q=cam.view([x,hf(x,z),z]);return q[2]>.4?[cam.cx+q[0]*cam.f/q[2],cam.cy-q[1]*cam.f/q[2]]:null;};ps3Bg(T);
 for(let xi=-24;xi<=24;xi++){const pts=[];for(let z=2;z<30;z+=.7){const p=P(xi*.55,z);if(p)pts.push(p);}gStroke(T,pts,[120,170,255],.8,.22,true);}
 for(let zi=0;zi<38;zi++){const z=2+zi*.75,pts=[];for(let x=-14;x<=14;x+=.5){const p=P(x,z);if(p)pts.push(p);}gStroke(T,pts,[150,190,255],.8,.3*clamp(1.3-z/30,0,1),true);}}
function ps3Twin(T,t){const {W,H}=T,cam=camera([0,0,0],0,0,W*.62,W/2,H*.43);ps3Bg(T);
 for(const side of [-1,1])for(let zi=0;zi<36;zi++){const z=2+mod(zi*.8-t*1.2,28.8);for(let xi=-24;xi<=24;xi++){const x=xi*.5,y=side*(2+.45*Math.sin(x*.35+t*.7+side)*Math.cos(z*.25-t*.4)),p=cam.view([x,y,z]);if(p[2]<.5)continue;const k=clamp(1.2-z/28,0,1);gPix(T,cam.cx+p[0]*cam.f/p[2],cam.cy-p[1]*cam.f/p[2],[130+k*110,170+k*70,255],.3+k*.55,true);}}}

// =====================================================================
// Consoles and variants
// =====================================================================
const CONSOLES={
 nes:{label:'NES',name:'Nintendo Entertainment System',accent:'#9B7E6B',W:256,H:144,pixel:true,post:{}},
 snes:{label:'SNES',name:'Super Nintendo',accent:'#8D66C4',W:320,H:180,pixel:true,post:{}},
 gbc:{label:'GBC',name:'Game Boy Color',accent:'#4FAE9C',W:160,H:90,pixel:true,post:{scale:3,grid:'lcd',ghost:.45,bits:5}},
 gba:{label:'GBA',name:'Game Boy Advance',accent:'#7065A7',W:240,H:135,pixel:true,post:{bits:5}},
 gamecube:{label:'GC',name:'GameCube',accent:'#7B68C9',W:480,H:270,pixel:false,post:{bloom:{thr:200,amt:.5}}},
 nds:{label:'DS',name:'Nintendo DS',accent:'#7580B9',W:DW,H:DH,pixel:true},
 wii:{label:'Wii',name:'Wii',accent:'#49B3C9',W:480,H:270,pixel:false,post:{bloom:{thr:240,amt:.3}}},
 '3ds':{label:'3DS',name:'Nintendo 3DS',accent:'#C0568E',W:FW,H:FH,pixel:true},
 megadrive:{label:'MD',name:'Mega Drive / Genesis',accent:'#3A6D74',W:320,H:180,pixel:true,post:{bits:3,composite:true}},
 dreamcast:{label:'DC',name:'Dreamcast',accent:'#F07C3E',W:480,H:270,pixel:false,post:{}},
 playstation:{label:'PS',name:'PlayStation',accent:'#8A8FA3',W:320,H:180,pixel:true,post:{bits:5,dither:true}},
 playstation2:{label:'PS2',name:'PlayStation 2',accent:'#3D6DB5',W:PW,H:PH,pixel:false},
 psp:{label:'PSP',name:'PSP',accent:'#596EBC',W:480,H:270,pixel:false,post:{ghost:.25}},
 playstation3:{label:'PS3',name:'PlayStation 3',accent:'#2E3A87',W:480,H:270,pixel:false,post:{bloom:{thr:150,amt:.7}}},
 arcade:{label:'Arcade',name:'Arcade',accent:'#C0473A',W:320,H:180,pixel:false,post:{scale:2,grid:'crt',bloom:{thr:140,amt:.6}}},
 steam:{label:'Steam',name:'Steam',accent:'#66C0F4',W:480,H:270,pixel:false,post:{}}};
const fixed=(con,id,name,cap,why,render)=>({con,id,name,cap,why,W:CONSOLES[con].W,H:CONSOLES[con].H,post:{},pixel:CONSOLES[con].pixel,render});
const legacy=(con,id,name)=>fixed(con,id,name,LEGACY.info[id],'From Codex’s SNES/PS1 lab, unchanged.',(L,t)=>LEGACY.painters[id](L.s,t));
const ps2=(id,name,fn,cap,why,opts={})=>fixed('playstation2',id,name,cap,why,(L,t,dt)=>{fn(t,dt);pPresent(L.ctx,L.img,L.frame,opts);});
const n3=(id,name,fn,cap,why)=>fixed('3ds',id,name,cap,why,(L,t)=>{fn(t);fPresent(L.ctx,L.img);});
const ds=(id,name,fn,cap,why,edge=false)=>fixed('nds',id,name,cap,why,(L,t)=>{fn(t);dPresent(L.ctx,L.img,{edge});});
const gen=(con,id,name,fn,cap,why,o={})=>{const C=CONSOLES[con],W=o.W||C.W,H=o.H||C.H,post=Object.assign({},C.post||{},o.post||{});return {con,id,name,cap,why,W,H,post,pixel:o.pixel??C.pixel,render:(L,t)=>{const T=gT(W,H);fn(T,t,L);gOut(L,T,post);}};};
const HALF={W:240,H:135},MID={W:320,H:180};
const ps2Trails2=fixed('playstation2','ps2-trails-v2','Light trails on glass','Glowing particle streams orbit the folder and are mirrored in a dark glossy floor.','The PS2 frame-feedback trails over a mirrored floor, the PS2-era trick of drawing the scene twice.',(L,t,dt)=>{
 if(L.state.tb){pc.set(L.state.tb);trail.ready=true;}else trail.ready=false;ps2Trails(t,dt);(L.state.tb||(L.state.tb=new Float32Array(PN*3))).set(pc);const hy=178;
 for(let y=hy;y<PH;y++){const my=2*hy-y,k=.38*(1-(y-hy)/(PH-hy));for(let x=0;x<PW;x++){const j=(y*PW+x)*3,m=(my*PW+x)*3;pc[j]=pc[j]*.55+pc[m]*k;pc[j+1]=pc[j+1]*.55+pc[m+1]*k;pc[j+2]=pc[j+2]*.58+pc[m+2]*k;}}
 for(let x=0;x<PW;x++){const j=(hy*PW+x)*3;pc[j]+=40;pc[j+1]+=50;pc[j+2]+=80;}pPresent(L.ctx,L.img,L.frame,{thr:120,amt:.7});});
const ps2Trails3=fixed('playstation2','ps2-trails-v3','Light trails over tiles','Glowing particle streams orbit the folder above a dark glass floor of faint tiles that mirrors them.','Improved again: the floor now has tile lines in perspective like Glass towers, so the trails clearly sit in a space.',(L,t,dt)=>{
 if(L.state.tb){pc.set(L.state.tb);trail.ready=true;}else trail.ready=false;ps2Trails(t,dt);(L.state.tb||(L.state.tb=new Float32Array(PN*3))).set(pc);const hy=172,f=300;
 for(let y=hy+1;y<PH;y++){const my=2*hy-y,k=.36*(1-(y-hy)/(PH-hy)),dist=1.6*f/(y-hy),zl=mod(dist+t*1.2,2)<.06*dist/6+.02;for(let x=0;x<PW;x++){const j=(y*PW+x)*3,m=(my*PW+x)*3,X=(x-240)*dist/f,line=zl||mod(X,2)<.05*dist/6+.02,g=line?22*clamp(1.4-dist/24,0,1):0;pc[j]=pc[j]*.5+pc[m]*k+g*.7;pc[j+1]=pc[j+1]*.5+pc[m+1]*k+g*.85;pc[j+2]=pc[j+2]*.55+pc[m+2]*k+g*1.4;}}
 for(let x=0;x<PW;x++){const j=(hy*PW+x)*3;pc[j]+=40;pc[j+1]+=50;pc[j+2]+=80;}pPresent(L.ctx,L.img,L.frame,{thr:120,amt:.7});});
const VARIANTS=[
 // NES: four Do
 gen('nes','nes-flow','Palette flow',nesFlow,'Beveled 16×16 blocks carry diagonal bands of blue that flow across the screen by palette cycling alone.','Colour moves only by cycling the 4-colour palette, built from the same blocks as Attribute ripple.'),
 gen('nes','nes-stars','Starfield flicker',nesStars2,'Star sprites drift over a dithered navy-and-violet nebula, twinkling, with crowded rows flickering.','A full-frame dithered tile layer with the 8-sprites-per-line flicker on top.'),
 gen('nes','nes-meta','Attribute ripple',nesMeta,'Rounded 16×16 blocks change palette in a ripple spreading out from the folder.','Palettes on the NES apply per 16×16 attribute block, so colour changes travel in blocky steps.'),
 gen('nes','nes-raster','Raster floor',nesRaster,'A striped checker floor rushes toward the horizon and gently curves, under a dithered sky.','Per-scanline scroll and palette stripes building a 3D floor, NES-racer style.'),
 // SNES
 legacy('snes','runway','Prism runway'),
 gen('snes','snes-mosaic-v2','Beveled mosaic',snesMosaic2,'Big beveled pastel tiles sway in a gentle wave while light moves across them.','Large 16×16 beveled tiles, a gentle warp and a calm centre.'),
 gen('snes','snes-lake','Mode 7 lake',snesLake,'A Mode 7 water plane ripples toward the horizon, wobbling per scanline and glinting with palette-cycled sparkles.','Mode 7 perspective combined with HDMA line wobble.'),
 gen('snes','snes-lake-v2','Mode 7 lake',snesLake2,'The same lake with no white sparkles: calmer ripple bands, a slowly turning plane and an HDMA-banded sky.','Improved for your note “there are weird white blimps”: the palette-cycled sparkles are gone and the water reads as soft colour bands instead.'),
 legacy('snes','tiles','Floating tiles'),
 // GBC: four Do
 gen('gbc','gbc-attr','Tile palettes',gbcAttr,'Beveled 8×8 tiles change colour in a ripple that travels out from the folder.','The GBC added per-tile colour attributes, so the ripple swaps palettes tile by tile.'),
 gen('gbc','gbc-bands','Palette bands',gbcBands,'Soft colour bands drift down the screen over a faint tile checker.','Palettes rewritten between scanlines, producing gradients otherwise impossible with 4 colours per tile.'),
 gen('gbc','gbc-sheen-v2','Glare lattice',gbcSheen2,'A beveled diamond lattice over drifting palette bands, with a soft glare sliding across.','The unlit GBC screen catching a lamp, over the palette-band surface.'),
 gen('gbc','gbc-confetti-v2','Confetti',gbcConfetti2,'Confetti in two sizes tumbles over soft palette bands with LCD smear.','15-bit colour at 1-pixel scale, softened by LCD ghosting.'),
 // GBA
 gen('gba','gba-voxel','Voxel hills',gbaVoxel,'Soft violet hills glide past in a voxel landscape, with a valley kept open for the folder.','Bitmap-mode voxel terrain.'),
 gen('gba','gba-voxel-v2','Terraced hills',gbaVoxel2,'Chunky terraced hills in teal, violet and pink step past under a banded sky.','Improved for your note “Maybe not for GBA?”: drawn in 2-pixel columns with height snapped into flat colour terraces, so it reads as a handheld tile world rather than a PC voxel engine.'),
 gen('gba','gba-corridor','Raycast corridor',gbaCorridor,'The camera drifts down a textured corridor of panelled walls and lit windows into violet fog.','Software raycasting as an endless calm corridor.'),
 gen('gba','gba-corridor-v2','Bright corridor',gbaCorridor2,'A light cream-and-lavender corridor with teal windows, its walls softly reflected in a checker floor.','Improved: lighter and calmer, and the floor reflects the walls using the GBA’s hardware alpha blend.'),
 gen('gba','gba-iso','Isometric blocks',gbaIso,'A field of isometric blocks rises and falls in waves, flat around the folder.','Bright isometric tile worlds were everywhere on GBA.'),
 gen('gba','gba-twist','Twisting floor',gbaTwist,'A tiled floor twists more with distance, like a slowly wringing ribbon.','Per-scanline affine parameters let each line rotate differently.'),
 gen('gba','gba-twist-v2','Twisting hall',gbaTwist2,'Floor and ceiling of beveled tiles twist in opposite directions toward a bright horizon.','Improved: fills the whole frame, with beveled tiles like Isometric blocks (which you liked) and a calmer palette.'),
 // GameCube
 gen('gamecube','gc-caustics','Caustic floor',gcCaustics,'Underwater light caustics dance across an indigo floor toward the horizon.','Indirect texturing warping light patterns in real time.',HALF),
 gen('gamecube','gc-cubes-v2','Cube field',gcField,'A floor of glossy indigo cubes rises and falls in a wave, stretching to the horizon.','The cube as a full-frame field, glossy and per-pixel lit.',MID),
 gen('gamecube','gc-cubes-v3','Cube ripple',gcField2,'A denser field of small glossy cubes ripples out in rings under a slowly circling light.','Improved: smaller, denser cubes, a circular ripple, a moving light and a gliding camera, so it moves more like water than a bar chart.',MID),
 gen('gamecube','gc-fur','Velvet shells',gcFur,'A field of fine violet fibres sways in slow gusts, catching light at the tips.','Shell-based fur shading as a plush surface.',MID),
 gen('gamecube','gc-lens','Glass lenses',gcLens,'Glass lenses glide over a grid and bend it as they pass.','Indirect-texture refraction as moving lenses.',HALF),
 // DS
 ds('ds-ripples','Tap ripples',dsRipples,'Taps land around the folder and send out crisp pixel rings.','Touch-screen feedback in 1px rings that never get smoothed.'),
 ds('ds-ripplefloor','Ripple floor',dsRippleFloor,'Taps land on a perspective sheet of graph paper and their ripples bend the grid lines as they spread.','Tap ripples on a 1px grid surface with stepped DS fog.'),
 ds('ds-ripplefloor-v2','Ripple floor',dsRippleFloor2,'A finer, lighter graph-paper floor where each tap leaves a mark and ripples lift and sink the grid in ice blue and navy.','Improved: a finer grid, deeper ripples with crests and troughs coloured differently, and a visible tap mark, so the motion is easy to read.'),
 ds('ds-dots','Dot tunnel',dsDots,'Two rippling planes of pixel dots stream past above and below.','Chunky 256-pixel 3D drawn as single dots, with stepped DS fog.'),
 ds('ds-flip','Flipbook doodles',dsFlip,'Hand-drawn doodles boil and morph into each other at 8 frames a second.','Two-colour 1px flipbook lines on ruled paper.'),
 // Wii
 gen('wii','wii-pointer-v2','Pointer field',wiiPointer2,'A blue pointer glides over a field of dots that part around it, and clicks send waves through the field.','The Wii Remote pointer moving through a surface it disturbs.'),
 gen('wii','wii-tiles','Tile floor',wiiTiles,'A soft white floor of rounded tiles recedes into light while a blue sheen sweeps over it.','Clean rounded white tiles and Wii blue.'),
 gen('wii','wii-stripes','Pinstripe drape',wiiStripes,'The Wii’s faint pinstripes bend over a slowly rolling surface, catching soft blue light.','The system-menu pinstripe texture as a draped surface.',MID),
 gen('wii','wii-stripes-v2','Pinstripe drape',wiiStripes2,'Broader, slower folds in the pinstripe sheet, with soft shading and a pale blue glow in the raised areas.','Improved: larger gentler folds, clearer light and shade, and finer stripes so it reads as fabric.',MID),
 // 3DS
 n3('3ds-tiles','Tile wave',n3Tiles,'A grid of glossy pillow tiles pops toward you in a slow wave.','Things pushing out of the screen: rounded tiles with crisp specular and rim light.'),
 n3('3ds-floorwave','Tile floor wave',n3FloorWave,'The same glossy pillow tiles laid as a floor, lifting in a slow ring wave as the view sways.','New, from the one 3DS idea you liked: Tile wave turned into a floor so the stereo depth runs into the distance.'),
 n3('3ds-tapwave','Tap wave',n3TapWave,'Tile wave, but the tiles pop out in rings from random touch points instead of one central wave.','New, also from Tile wave: the 3DS touch screen driving the pop-out.'),
 // Mega Drive
 gen('megadrive','md-speed-v2','Blast floor',mdSpeed2,'Speed streaks race over a curving line-scrolled checker floor.','Mega Drive line scroll with speed streaks.'),
 gen('megadrive','md-speed-v3','Blast horizon',mdSpeed3,'A raster-banded sky with a bright horizon line, a dithered fade into the floor, and fewer, longer streaks.','Improved: the sky now uses 9-bit raster bands and the streaks stay in the sky, so the floor and sky read as one scene.'),
 gen('megadrive','md-planes','Twin planes',mdPlanes,'A checker floor and a teal ceiling slide in opposite directions toward the horizon.','The two Mega Drive background planes, each line-scrolled into perspective.'),
 gen('megadrive','md-pipe','Half-pipe',mdPipe,'A curving checker half-pipe streams toward you and bends gently.','Fast pseudo-3D checker tubes of 16-bit Sega bonus stages.'),
 gen('megadrive','md-pipe-v2','Checker tube',mdPipe2,'A full checker tube wraps all the way around and bends gently, darker overhead.','Improved for your note “change upper part so it doesn’t look broken in half”: the tube now continues overhead, so there is no split.'),
 // Dreamcast
 gen('dreamcast','dc-fog-v2','Block field',dcFog2,'A field of white, orange and sky-blue blocks rises and falls as it streams out of bright fog.','Hardware fog in daylight colours.',MID),
 gen('dreamcast','dc-fog-v3','Block field',dcFog3,'Mostly white blocks with a few orange ones ripple out of a warm, soft fog as the camera glides.','Improved: a calmer, more cohesive palette, a smoother wave and a moving camera.',MID),
 gen('dreamcast','dc-sea','Blue sea',dcSea,'A faceted turquoise sea rolls under a Sega-blue sky, sparkling where it catches the sun.','Crisp VGA polygons and saturated daylight.',MID),
 gen('dreamcast','dc-sea-v2','Blue sea',dcSea2,'A smoother, denser turquoise sea with tighter, brighter sun glints.','Improved: smooth shading instead of faceted, so the sea looks clean and sunny rather than low-poly.',MID),
 gen('dreamcast','dc-checker','Sunny checker',dcChecker,'An orange-and-white checker floor turns slowly under a bright blue sky, with a soft gloss.','Clean, sunny Sega arcade energy as a floor.'),
 gen('dreamcast','dc-checker-v2','Glossy checker',dcChecker2,'A softer peach-and-cream checker that mirrors the sky and glows at the horizon.','Improved: gentler colours and a real sky reflection, so it feels polished rather than loud.'),
 // PlayStation
 legacy('playstation','field','Folded field'),legacy('playstation','tide','Crystal tide'),legacy('playstation','frames','Passing frames'),
 gen('playstation','ps1-terrace','Textured terraces',ps1Terrace,'A field of textured stepped blocks shifts level by level as the camera drifts over it.','Affine textures, snapped vertices, fog and 15-bit dither.'),
 gen('playstation','ps1-terrace-v2','Textured terraces',ps1Terrace2,'Smaller blocks in a denser field that step through finer levels, with a lower, slower camera.','Improved: finer steps so the motion no longer jumps, and a denser field.'),
 gen('playstation','ps1-pipe','Textured pipe',ps1Pipe,'The camera glides down a gently bending tube of textured panels.','Passing frames as a continuous textured surface.'),
 gen('playstation','ps1-pipe-v2','Textured pipe',ps1Pipe2,'A wider, slower tube with larger panels and a ring rhythm along its length.','Improved: slower and wider with fewer, larger panels, closer in feel to Passing frames.'),
 // PS2
 ps2('ps2-towers','Glass towers',ps2Towers,'See-through columns rise and fall over a reflective floor.','Alpha-blended overdraw, a mirrored reflection pass, smooth shading and fog.',{thr:228,amt:.4}),
 ps2Trails2,ps2Trails3,
 ps2('ps2-sea','Chrome sea',ps2Sea,'A smooth reflective surface rolls toward a hazy low sun.','Per-vertex environment reflection, Fresnel and glints in heavy fog and bloom.',{thr:228,amt:.4}),
 ps2('ps2-orbs-v2','Pearl orbs on glass',ps2Orbs2,'Glossy spheres circle the folder above a pale glass floor that reflects them.','The orbs over a reflective floor, like Glass towers.',{thr:228,amt:.4}),
 ps2('ps2-orbs-v3','Rolling pearls',ps2Orbs3,'Seven large pearls roll in a slow circle on a tiled glass floor, mirrored beneath them.','Improved: the orbs now rest and roll on the floor, larger and fewer, so they feel physical instead of floating.',{thr:228,amt:.4}),
 // PSP
 gen('psp','psp-field','Theme wave',pspField,'A smooth wave surface in the month’s theme colour rolls toward you, crests catching light.','The flowing-wave idea as a 3D surface.',MID),
 gen('psp','psp-field-v2','Theme wave',pspField2,'The wave surface with bright ribbon lines tracing three crests across it, on a deeper gradient.','Improved: thin glowing lines ride the surface, the PSP menu-wave signature, and the darker sky gives contrast.',MID),
 gen('psp','psp-ribbon','Glass ribbons',pspRibbon,'Two translucent twisting ribbons flow across the theme gradient with bright edges.','New, from Theme wave: the PSP wave as 3D ribbons with real twist and depth.',MID),
 gen('psp','psp-tiles','Glow floor',pspTiles,'A piano-black tile floor whose seams light up in the theme colour as a wave passes.','New: glossy black and the theme hue as a floor surface.',HALF),
 // PS3
 gen('playstation3','ps3-sheet','Particle sheet',ps3Sheet,'Hundreds of glowing particles form a rolling sheet that streams toward you, leaving trails.','SPU-scale particle counts as a surface with depth.',{post:{persist:.8}}),
 gen('playstation3','ps3-wire','Light mesh',ps3Wire,'The same rolling sheet drawn as a glowing wire mesh.','New, from Particle sheet (which you liked): lines instead of points, with HDR glow.',{post:{persist:.7}}),
 gen('playstation3','ps3-twin','Particle tunnel',ps3Twin,'Particle sheets above and below stream toward you, forming a tunnel of light.','New, from Particle sheet: two sheets framing the folder.',{post:{persist:.8}}),
 // Arcade
 gen('arcade','arc-vector-v2','Vector grid',arcVector2,'Glowing wireframe shapes turn above a scrolling vector grid floor, with phosphor afterglow.','Vector monitor lines over a grid floor.',{post:{persist:.82}}),
 gen('arcade','arc-vector-v3','Vector valley',arcVector3,'A wireframe valley scrolls toward you between rolling vector hills, with one shape turning above.','Improved: the grid becomes a terrain that fills the frame, and there is one shape instead of four.',{post:{persist:.8}}),
 gen('arcade','arc-bulbs','Bulb wall',arcBulbs,'A whole wall of bulbs plays slow chase patterns, framed by a running border.','The cabinet’s marquee bulbs filling the frame.'),
 gen('arcade','arc-bulbs-v2','Bulb wall',arcBulbs2,'Larger, warmer bulbs with slow rings of light and the odd twinkle, and a gentler border chase.','Improved: calmer patterns and bigger bulbs, so it glows instead of flashing.'),
 gen('arcade','arc-checker','Checker floor',arcChecker,'A green-and-cream checker floor races toward a pink horizon, swaying side to side.','The classic arcade checkerboard floor.'),
 gen('arcade','arc-plating','Hull plating',arcPlating,'Riveted metal plates scroll slowly downward, with hatches glowing cyan.','The scrolling metal floor of vertical shooters, under CRT scanlines.'),
 // Steam
 gen('steam','steam-wall-v2','Library hall',steamWall2,'Walls of game covers slide past on both sides, reflected in a dark glossy floor.','Your library as architecture.'),
 gen('steam','steam-mesh','Low-poly terrain',steamMesh,'A flat-shaded low-poly landscape in Steam blues glides toward you.','A modern low-poly surface.',MID),
 gen('steam','steam-grid','Library floor',steamGrid,'A dark floor of cover-shaped panels where individual tiles softly light up.','Your library as a surface you glide over.',HALF),
 gen('steam','steam-grid-v2','Library floor',steamGrid2,'Portrait cover tiles with two-tone “art”, soft individual glows and a slow light sweep.','Improved: each tile now looks like a cover rather than a blank panel.',HALF)];
const RATED={do:['nes-flow','nes-stars','nes-meta','nes-raster','runway','snes-mosaic-v2','tiles','gbc-attr','gbc-bands','gbc-sheen-v2','gbc-confetti-v2','gba-iso','gc-caustics','gc-fur','gc-lens','ds-ripples','ds-dots','ds-flip','wii-pointer-v2','wii-tiles','3ds-tiles','md-planes','field','tide','frames','ps2-towers','ps2-sea','ps3-sheet','arc-checker','arc-plating','steam-wall-v2','steam-mesh'],maybe:['snes-lake','gba-voxel','gba-corridor','gba-twist','gc-cubes-v2','ds-ripplefloor','wii-stripes','md-speed-v2','md-pipe','dc-fog-v2','dc-sea','dc-checker','ps1-terrace','ps1-pipe','ps2-trails-v2','ps2-orbs-v2','psp-field','arc-vector-v2','arc-bulbs','steam-grid']};
const HISTORY={};for(const v of VARIANTS){const base=v.id.replace(/-v\d$/,''),prev=VARIANTS.find(o=>o!==v&&RATED.maybe.includes(o.id)&&(o.id===base||o.id.replace(/-v\d$/,'')===base));
 HISTORY[v.id]=RATED.do.includes(v.id)?{kind:'kept',text:'Do',seed:'do'}:RATED.maybe.includes(v.id)?{kind:'compare',text:'Current (Maybe)',seed:'maybe'}:prev?{kind:'improved',text:'Improved version (compare with the card before)'}:{kind:'new',text:'New'};}
function outSize(v){const s=(v.post&&v.post.scale)||1;return [v.W*s,v.H*s];}
function prepare(L,v,startTime=0){const [ow,oh]=outSize(v);if(L.canvas.width!==ow||L.canvas.height!==oh||!L.img){L.canvas.width=ow;L.canvas.height=oh;L.img=L.ctx.createImageData(ow,oh);}
 L.s={ctx:L.ctx,image:L.img,data:L.img.data,depth:new Float32Array(v.W*v.H)};L.canvas.classList.toggle('pixel',!!v.pixel);L.v=v;L.state={};L.frame=0;L.time=startTime;}
function render(L,dt){L.v.render(L,L.time,dt||1/30);L.frame++;}
function makeLayer(canvas){return {canvas,ctx:canvas.getContext('2d',{alpha:false}),img:null,s:null,v:null,state:{},time:0,frame:0};}
window.Backdrops={CONSOLES,VARIANTS,HISTORY,RATED,prepare,render,makeLayer,outSize};

})();
