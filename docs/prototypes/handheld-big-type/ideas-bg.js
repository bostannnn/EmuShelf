// EmuShelf Big Type proposals: ten candidate "default backgrounds" for the things that are not a
// console (Settings, Activity, the user's own folders, the Start menu). Loaded by proposals.html,
// which reads window.BT_IDEAS_BG. Prototype code only: CPU software rendering into a small float
// buffer, in the manner of ../handheld-mode/backdrops-motion-study/backdrops.js (triangle
// rasteriser with a depth buffer, fog, additive splats, bloom on output). Nothing here is approved.
//
// Every draw(ctx, t, c) is a pure function of the time t (seconds) and the colour c ([r, g, b]):
// no state is carried between frames, so any single frame can be drawn on its own and the colour
// can change at any moment. Each palette is built only from c: tints toward white, c itself, and
// shades toward a near-black of the same hue. The lower left is kept darker for the white name.
(()=>{
'use strict';
// ---------- maths ----------
const TAU=Math.PI*2;
const clamp=(v,a,b)=>v<a?a:v>b?b:v,lerp=(a,b,t)=>a+(b-a)*t,fract=v=>v-Math.floor(v);
const smooth=(a,b,x)=>{const t=clamp((x-a)/(b-a),0,1);return t*t*(3-2*t);};
const hash=n=>{const s=Math.sin(n*127.1+311.7)*43758.5453;return s-Math.floor(s);};
const hash2=(a,b)=>hash(a*12.9898+b*78.233+.5);
const vnoise=x=>{const i=Math.floor(x),f=x-i;return lerp(hash(i),hash(i+1),f*f*(3-2*f));};
const mix3=(a,b,t)=>[a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t,a[2]+(b[2]-a[2])*t];
const bayer=[0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5];
// The palette: sh(k) shades the colour toward a near-black of its own hue, ti(k) tints it toward white.
function tones(c){const ink=[c[0]*.05+4,c[1]*.05+4,c[2]*.05+8];return {c,ink,sh:k=>mix3(c,ink,k),ti:k=>mix3(c,[255,255,255],k)};}

// ---------- render target: float RGB + depth (stored as 1/z, larger is nearer) ----------
let T=null,C=null,D=null,TW=0,TH=0;
const TG={};
function mkCanvas(w,h){const cv=document.createElement('canvas');cv.width=w;cv.height=h;return cv;}
function target(W,H){const key=W+'x'+H;let t=TG[key];if(t)return t;const N=W*H;
 t=TG[key]={W,H,N,c:new Float32Array(N*3),d:new Float32Array(N),veil:new Float32Array(N),lum:new Float32Array(N),img:new ImageData(W,H),cv:null,cx:null};
 // where the name sits: the left two thirds, strongest in the lower part
 for(let y=0;y<H;y++)for(let x=0;x<W;x++)t.veil[y*W+x]=smooth(.8,.3,x/W)*(.45+.55*smooth(.02,.5,y/H));
 t.bw=W>>2;t.bh=H>>2;t.bcv=mkCanvas(t.bw,t.bh);t.bx=t.bcv.getContext('2d');t.bimg=new ImageData(t.bw,t.bh);
 t.bcv2=mkCanvas(Math.max(1,t.bw/3|0),Math.max(1,t.bh/3|0));t.bx2=t.bcv2.getContext('2d');return t;}
function use(t){T=t;C=t.c;D=t.d;TW=t.W;TH=t.H;D.fill(0);}
function fill(col){for(let i=0,n=T.N*3;i<n;i+=3){C[i]=col[0];C[i+1]=col[1];C[i+2]=col[2];}}
// Output: edges smoothed, the veil for the name, a half-level ordered dither against banding, then
// a two-step bloom. The smoothing is the edge half of FXAA: where brightness steps sharply it walks
// along the edge both ways to find how long the step is, and slides the pixel toward its neighbour
// by its place on that step, so long stair-steps become ramps while flat areas stay untouched.
// Pass aa = false for pictures made of sparks and soft light, which have no hard edges to smooth.
function blit(ctx,img){const cv=ctx.canvas;if(cv.width===TW&&cv.height===TH){ctx.putImageData(img,0,0);return;}
 if(!T.cv){T.cv=mkCanvas(TW,TH);T.cx=T.cv.getContext('2d');}T.cx.putImageData(img,0,0);ctx.save();ctx.imageSmoothingEnabled=true;ctx.drawImage(T.cv,0,0,cv.width,cv.height);ctx.restore();}
function present(ctx,veil,thr,amt,aa){const img=T.img,d=img.data,V=T.veil,L=T.lum,W=TW,H=TH;aa=aa!==false;
 if(aa)for(let i=0,j=0,n=T.N;i<n;i++,j+=3)L[i]=C[j]*.3+C[j+1]*.55+C[j+2]*.15;
 for(let y=0,i=0;y<H;y++){const by=(y&3)*4,inner=y>0&&y<H-1;for(let x=0;x<W;x++,i++){const j=i*3,p=i*4,k=1-veil*V[i],dt=bayer[by+(x&3)]/16-.5;let r=C[j],g=C[j+1],b=C[j+2];
  if(aa&&inner&&x>0&&x<W-1){const m=L[i],n=L[i-W],s=L[i+W],w=L[i-1],e=L[i+1];let lo=m,hi=m;if(n<lo)lo=n;else if(n>hi)hi=n;if(s<lo)lo=s;else if(s>hi)hi=s;if(w<lo)lo=w;else if(w>hi)hi=w;if(e<lo)lo=e;else if(e>hi)hi=e;
   if(hi-lo>5+hi*.07&&!(m>n&&m>s&&m>w&&m>e)){   // a lone bright point is a spark, not an edge: leave it
   const hor=Math.abs(n+s-2*m)>=Math.abs(w+e-2*m),l1=hor?n:w,l2=hor?s:e,g1=Math.abs(l1-m),g2=Math.abs(l2-m),first=g1>=g2,st=(hor?W:1)*(first?-1:1),gs=.25*(first?g1:g2),la=.5*((first?l1:l2)+m),al=hor?1:W,m1=hor?x:y,m2=hor?W-1-x:H-1-y;
    let d1=1,d2=1,e1=0,e2=0;for(;d1<=12&&d1<=m1;d1++){const q=i-d1*al;e1=.5*(L[q]+L[q+st])-la;if(e1>=gs||e1<=-gs)break;}for(;d2<=12&&d2<=m2;d2++){const q=i+d2*al;e2=.5*(L[q]+L[q+st])-la;if(e2>=gs||e2<=-gs)break;}
    let f=((m-la)<0)!==((d1<d2?e1:e2)<0)?.5-(d1<d2?d1:d2)/(d1+d2):0,sb=Math.abs((n+s+w+e)*.25-m)/(hi-lo);sb=sb>1?1:sb;sb=sb*sb*(3-2*sb);sb*=sb*.75;if(sb>f)f=sb;   // short steps and corners: a plain blend
    const q=(i+st)*3;r+=(C[q]-r)*f;g+=(C[q+1]-g)*f;b+=(C[q+2]-b)*f;}}
  d[p]=r*k+dt;d[p+1]=g*k+dt;d[p+2]=b*k+dt;d[p+3]=255;}}
 blit(ctx,img);if(!(amt>0))return;
 const bw=T.bw,bh=T.bh,bd=T.bimg.data,inv=1/(255-thr);
 for(let by=0;by<bh;by++)for(let bx=0;bx<bw;bx++){let r=0,g=0,b=0;for(let yy=0;yy<4;yy++){let j=((by*4+yy)*TW+bx*4)*3;for(let xx=0;xx<4;xx++,j+=3){r+=C[j];g+=C[j+1];b+=C[j+2];}}
  const k=(1-veil*V[(by*4+2)*TW+bx*4+2])/16;r*=k;g*=k;b*=k;let s=((r*.3+g*.55+b*.15)-thr)*inv;s=s<0?0:s>1.5?1.5:s;const p=(by*bw+bx)*4;bd[p]=r*s;bd[p+1]=g*s;bd[p+2]=b*s;bd[p+3]=255;}
 T.bx.putImageData(T.bimg,0,0);T.bx2.imageSmoothingEnabled=true;T.bx2.drawImage(T.bcv,0,0,T.bcv2.width,T.bcv2.height);
 const cv=ctx.canvas;ctx.save();ctx.imageSmoothingEnabled=true;ctx.globalCompositeOperation='lighter';ctx.globalAlpha=amt;ctx.drawImage(T.bcv,0,0,cv.width,cv.height);ctx.globalAlpha=amt*.9;ctx.drawImage(T.bcv2,0,0,cv.width,cv.height);ctx.restore();}

// ---------- camera ----------
const cam={px:0,py:0,pz:0,rx:1,ry:0,rz:0,ux:0,uy:1,uz:0,fx:0,fy:0,fz:1,f:300,ox:240,oy:135};
let SX=0,SY=0,SZ=0;const NEAR=.3;
// yaw turns toward +x, positive pitch looks down
function camYP(px,py,pz,yaw,pitch,f,ox,oy){const cw=Math.cos(yaw),sw=Math.sin(yaw),cp=Math.cos(pitch),sp=Math.sin(pitch);
 cam.px=px;cam.py=py;cam.pz=pz;cam.rx=cw;cam.ry=0;cam.rz=-sw;cam.ux=sw*sp;cam.uy=cp;cam.uz=cw*sp;cam.fx=sw*cp;cam.fy=-sp;cam.fz=cw*cp;cam.f=f;cam.ox=ox;cam.oy=oy;}
function camLook(px,py,pz,tx,ty,tz,f,ox,oy){let fx=tx-px,fy=ty-py,fz=tz-pz;const fl=1/Math.hypot(fx,fy,fz);fx*=fl;fy*=fl;fz*=fl;const rl=1/Math.hypot(fx,fz),rx=fz*rl,rz=-fx*rl;
 cam.px=px;cam.py=py;cam.pz=pz;cam.rx=rx;cam.ry=0;cam.rz=rz;cam.ux=fy*rz;cam.uy=fz*rx-fx*rz;cam.uz=-fy*rx;cam.fx=fx;cam.fy=fy;cam.fz=fz;cam.f=f;cam.ox=ox;cam.oy=oy;}
function proj(x,y,z){x-=cam.px;y-=cam.py;z-=cam.pz;const vz=x*cam.fx+y*cam.fy+z*cam.fz;SZ=vz;if(vz<NEAR)return false;const k=cam.f/vz;SX=cam.ox+(x*cam.rx+z*cam.rz)*k;SY=cam.oy-(x*cam.ux+y*cam.uy+z*cam.uz)*k;return true;}
// One screen row against the horizontal plane y = h: afterwards the world point under pixel x is
// (RX0 + x*RXS, h, RZ0 + x*RZS) and its view depth is RTH. False where the row misses the plane.
let RTH=0,RX0=0,RXS=0,RZ0=0,RZS=0;
function planeRow(y,h){const dy=-(y+.5-cam.oy)/cam.f,diry=cam.uy*dy+cam.fy,dh=h-cam.py;if(dh<0?diry>-1e-4:diry<1e-4)return false;
 const th=dh/diry,k=th/cam.f;RTH=th;RXS=cam.rx*k;RZS=cam.rz*k;RX0=cam.px+(cam.ux*dy+cam.fx)*th+(.5-cam.ox)*RXS;RZ0=cam.pz+(cam.uz*dy+cam.fz)*th+(.5-cam.ox)*RZS;return true;}

// ---------- triangles ----------
// Screen-space Gouraud triangle with a depth test. mode 0 opaque, 1 blend (tested, not written),
// 2 additive (tested), 3 additive (untested), 4 blend (untested), 5 blend and write depth.
function tri(x0,y0,w0,r0,g0,b0,x1,y1,w1,r1,g1,b1,x2,y2,w2,r2,g2,b2,al,mode){
 const e1x=x1-x0,e1y=y1-y0,e2x=x2-x0,e2y=y2-y0,area=e1x*e2y-e2x*e1y;if(!(area>1e-3||area<-1e-3))return;
 const ya=y0<y1?(y0<y2?y0:y2):(y1<y2?y1:y2),yb=y0>y1?(y0>y2?y0:y2):(y1>y2?y1:y2);
 let ys=Math.ceil(ya-.5),ye=Math.ceil(yb-.5)-1;if(ys<0)ys=0;if(ye>=TH)ye=TH-1;if(ys>ye)return;
 if((x0<0&&x1<0&&x2<0)||(x0>=TW&&x1>=TW&&x2>=TW))return;
 const ia=1/area,dwx=((w1-w0)*e2y-(w2-w0)*e1y)*ia,dwy=((w2-w0)*e1x-(w1-w0)*e2x)*ia;
 const flat=r0===r1&&r1===r2&&g0===g1&&g1===g2&&b0===b1&&b1===b2;let drx=0,dry=0,dgx=0,dgy=0,dbx=0,dby=0;
 if(!flat){drx=((r1-r0)*e2y-(r2-r0)*e1y)*ia;dry=((r2-r0)*e1x-(r1-r0)*e2x)*ia;dgx=((g1-g0)*e2y-(g2-g0)*e1y)*ia;dgy=((g2-g0)*e1x-(g1-g0)*e2x)*ia;dbx=((b1-b0)*e2y-(b2-b0)*e1y)*ia;dby=((b2-b0)*e1x-(b1-b0)*e2x)*ia;}
 for(let y=ys;y<=ye;y++){const py=y+.5;let xl=1e9,xr=-1e9,xx;
  if((y0<=py)!==(y1<=py)){xx=x0+(py-y0)*e1x/e1y;if(xx<xl)xl=xx;if(xx>xr)xr=xx;}
  if((y1<=py)!==(y2<=py)){xx=x1+(py-y1)*(x2-x1)/(y2-y1);if(xx<xl)xl=xx;if(xx>xr)xr=xx;}
  if((y2<=py)!==(y0<=py)){xx=x2+(py-y2)*(x0-x2)/(y0-y2);if(xx<xl)xl=xx;if(xx>xr)xr=xx;}
  let xs=Math.ceil(xl-.5),xe=Math.ceil(xr-.5)-1;if(xs<0)xs=0;if(xe>=TW)xe=TW-1;if(xs>xe)continue;
  const fx=xs+.5-x0,fy=py-y0;let w=w0+fx*dwx+fy*dwy,i=y*TW+xs,j=i*3,r=r0+fx*drx+fy*dry,g=g0+fx*dgx+fy*dgy,b=b0+fx*dbx+fy*dby;
  if(mode===0){for(let x=xs;x<=xe;x++,i++,j+=3,w+=dwx,r+=drx,g+=dgx,b+=dbx)if(w>D[i]){D[i]=w;C[j]=r;C[j+1]=g;C[j+2]=b;}}
  else if(mode===1||mode===5){const wr=mode===5;for(let x=xs;x<=xe;x++,i++,j+=3,w+=dwx,r+=drx,g+=dgx,b+=dbx)if(w>D[i]){if(wr)D[i]=w;C[j]+=(r-C[j])*al;C[j+1]+=(g-C[j+1])*al;C[j+2]+=(b-C[j+2])*al;}}
  else if(mode===2){for(let x=xs;x<=xe;x++,i++,j+=3,w+=dwx,r+=drx,g+=dgx,b+=dbx)if(w>D[i]){C[j]+=r*al;C[j+1]+=g*al;C[j+2]+=b*al;}}
  else if(mode===3){for(let x=xs;x<=xe;x++,j+=3,r+=drx,g+=dgx,b+=dbx){C[j]+=r*al;C[j+1]+=g*al;C[j+2]+=b*al;}}
  else{for(let x=xs;x<=xe;x++,j+=3,r+=drx,g+=dgx,b+=dbx){C[j]+=(r-C[j])*al;C[j+1]+=(g-C[j+1])*al;C[j+2]+=(b-C[j+2])*al;}}
 }
}
// A scratch vertex list: project with vtx(), colour with vcol(), then join by index.
const MAXV=8192,VX=new Float32Array(MAXV),VY=new Float32Array(MAXV),VW=new Float32Array(MAXV),VR=new Float32Array(MAXV),VG=new Float32Array(MAXV),VB=new Float32Array(MAXV),VOK=new Uint8Array(MAXV);
function vtx(k,x,y,z){if(!proj(x,y,z)){VOK[k]=0;return false;}VX[k]=SX;VY[k]=SY;VW[k]=1/SZ;VOK[k]=1;return true;}
function vcol(k,r,g,b){VR[k]=r;VG[k]=g;VB[k]=b;}
function tr(a,b,c,al,mode){if(VOK[a]&&VOK[b]&&VOK[c])tri(VX[a],VY[a],VW[a],VR[a],VG[a],VB[a],VX[b],VY[b],VW[b],VR[b],VG[b],VB[b],VX[c],VY[c],VW[c],VR[c],VG[c],VB[c],al,mode);}
function trf(a,b,c,r,g,bl,al,mode){if(VOK[a]&&VOK[b]&&VOK[c])tri(VX[a],VY[a],VW[a],r,g,bl,VX[b],VY[b],VW[b],r,g,bl,VX[c],VY[c],VW[c],r,g,bl,al,mode);}
function qf(a,b,c,d,r,g,bl,al,mode){trf(a,b,c,r,g,bl,al,mode);trf(a,c,d,r,g,bl,al,mode);}

// ---------- meshes: positions, normals, triangle indices ----------
function mesh(P,N,I){return {n:P.length/3,P:new Float32Array(P),N:new Float32Array(N),I:new Uint16Array(I)};}
function sphereMesh(nu,nv){const P=[],N=[],I=[];for(let j=0;j<=nv;j++)for(let i=0;i<=nu;i++){const th=i/nu*TAU,ph=j/nv*Math.PI,x=Math.sin(ph)*Math.cos(th),y=Math.cos(ph),z=Math.sin(ph)*Math.sin(th);P.push(x,y,z);N.push(x,y,z);}
 for(let j=0;j<nv;j++)for(let i=0;i<nu;i++){const a=j*(nu+1)+i,b=a+1,c=a+nu+1,d=c+1;if(j>0)I.push(a,b,d);if(j<nv-1)I.push(a,d,c);}return mesh(P,N,I);}
// A ring in the xy plane (its axis is z), major radius R, tube radius r.
function torusMesh(R,r,nu,nv){const P=[],N=[],I=[];for(let i=0;i<=nu;i++)for(let j=0;j<=nv;j++){const th=i/nu*TAU,ph=j/nv*TAU,cx=Math.cos(th),cy=Math.sin(th),k=Math.cos(ph);P.push((R+r*k)*cx,(R+r*k)*cy,r*Math.sin(ph));N.push(k*cx,k*cy,Math.sin(ph));}
 for(let i=0;i<nu;i++)for(let j=0;j<nv;j++){const a=i*(nv+1)+j,b=a+1,c=a+nv+1,d=c+1;I.push(a,b,d,a,d,c);}return mesh(P,N,I);}
// A shape turned on a lathe about y. The profile runs from the bottom outside, up, and in to the top centre.
function latheMesh(prof,segs){const P=[],N=[],I=[],m=prof.length;
 for(let k=0;k<m;k++){const a=prof[Math.max(0,k-1)],b=prof[Math.min(m-1,k+1)];let nr=b[1]-a[1],ny=-(b[0]-a[0]);const l=Math.hypot(nr,ny)||1;nr/=l;ny/=l;
  for(let i=0;i<=segs;i++){const th=i/segs*TAU,cx=Math.cos(th),cz=Math.sin(th);P.push(prof[k][0]*cx,prof[k][1],prof[k][0]*cz);N.push(nr*cx,ny,nr*cz);}}
 for(let k=0;k<m-1;k++)for(let i=0;i<segs;i++){const a=k*(segs+1)+i,b=a+1,c=a+segs+1,d=c+1;I.push(a,b,d,a,d,c);}return mesh(P,N,I);}
// Draw a mesh: world = M * (local * s) + t. shade(wx,wy,wz,nx,ny,nz,v) leaves the vertex colour in R_,G_,B_.
let R_=0,G_=0,B_=0;
function drawMesh(M,m,s,tx,ty,tz,shade,al,mode){const P=M.P,N=M.N,I=M.I;
 for(let v=0,p=0;v<M.n;v++,p+=3){const x=P[p]*s,y=P[p+1]*s,z=P[p+2]*s,wx=m[0]*x+m[1]*y+m[2]*z+tx,wy=m[3]*x+m[4]*y+m[5]*z+ty,wz=m[6]*x+m[7]*y+m[8]*z+tz;
  if(!vtx(v,wx,wy,wz))continue;const nx=N[p],ny=N[p+1],nz=N[p+2];shade(wx,wy,wz,m[0]*nx+m[1]*ny+m[2]*nz,m[3]*nx+m[4]*ny+m[5]*nz,m[6]*nx+m[7]*ny+m[8]*nz,v);VR[v]=R_;VG[v]=G_;VB[v]=B_;}
 for(let k=0;k<I.length;k+=3)tr(I[k],I[k+1],I[k+2],al,mode);}
// The usual material: a three-stop ramp (dark, mid, light) driven by one light, plus a highlight,
// a rim and fog. Set the fields of MT, then pass lit to drawMesh.
const MT={lx:0,ly:1,lz:0,amb:.3,d:[0,0,0],m:[0,0,0],l:[0,0,0],spec:0,shin:30,rim:0,rc:[255,255,255],fog:[0,0,0],fn:0,fr:1e9,k:1};
function lit(wx,wy,wz,nx,ny,nz){const dd=nx*MT.lx+ny*MT.ly+nz*MT.lz;let q=(MT.amb+(1-MT.amb)*(dd>0?dd:0))*MT.k;if(q>1)q=1;
 let vx=cam.px-wx,vy=cam.py-wy,vz=cam.pz-wz;const vl=1/Math.sqrt(vx*vx+vy*vy+vz*vz);vx*=vl;vy*=vl;vz*=vl;let ndv=nx*vx+ny*vy+nz*vz;if(ndv<0)ndv=-ndv;
 const hx=MT.lx+vx,hy=MT.ly+vy,hz=MT.lz+vz,nh=(nx*hx+ny*hy+nz*hz)/Math.sqrt(hx*hx+hy*hy+hz*hz),sp=nh>0?Math.pow(nh,MT.shin)*MT.spec*255:0,ri=(1-ndv)*(1-ndv)*(1-ndv)*MT.rim;
 const a=q<.5?MT.d:MT.m,b=q<.5?MT.m:MT.l,u=q<.5?q*2:q*2-1;let r=a[0]+(b[0]-a[0])*u+sp+MT.rc[0]*ri,g=a[1]+(b[1]-a[1])*u+sp+MT.rc[1]*ri,bl=a[2]+(b[2]-a[2])*u+sp+MT.rc[2]*ri;
 const k=clamp((SZ-MT.fn)/MT.fr,0,1);R_=r+(MT.fog[0]-r)*k;G_=g+(MT.fog[1]-g)*k;B_=bl+(MT.fog[2]-bl)*k;}
// Rotation matrices (row-major 3x3).
const IDENT=[1,0,0,0,1,0,0,0,1];
function rotAxis(kx,ky,kz,a,o){const c=Math.cos(a),s=Math.sin(a),q=1-c;o=o||new Array(9);o[0]=c+kx*kx*q;o[1]=kx*ky*q-kz*s;o[2]=kx*kz*q+ky*s;o[3]=ky*kx*q+kz*s;o[4]=c+ky*ky*q;o[5]=ky*kz*q-kx*s;o[6]=kz*kx*q-ky*s;o[7]=kz*ky*q+kx*s;o[8]=c+kz*kz*q;return o;}
function mmul(a,b,o){o=o||new Array(9);for(let r=0;r<3;r++)for(let c=0;c<3;c++)o[r*3+c]=a[r*3]*b[c]+a[r*3+1]*b[3+c]+a[r*3+2]*b[6+c];return o;}

// ---------- light: additive discs, dots and streaks ----------
function glow(x,y,rad,r,g,b,k){const x0=Math.max(0,Math.floor(x-rad)),x1=Math.min(TW-1,Math.ceil(x+rad)),y0=Math.max(0,Math.floor(y-rad)),y1=Math.min(TH-1,Math.ceil(y+rad)),ir=1/(rad*rad);
 for(let yy=y0;yy<=y1;yy++){const dy=yy+.5-y;for(let xx=x0;xx<=x1;xx++){const dx=xx+.5-x,q=1-(dx*dx+dy*dy)*ir;if(q>0){const w=q*q*k,j=(yy*TW+xx)*3;C[j]+=r*w;C[j+1]+=g*w;C[j+2]+=b*w;}}}}
function dot(x,y,r,g,b,k){const xi=Math.floor(x-.5),yi=Math.floor(y-.5);if(xi<0||yi<0||xi>=TW-1||yi>=TH-1)return;const fx=x-.5-xi,fy=y-.5-yi;let j=(yi*TW+xi)*3,w=(1-fx)*(1-fy)*k;
 C[j]+=r*w;C[j+1]+=g*w;C[j+2]+=b*w;w=fx*(1-fy)*k;C[j+3]+=r*w;C[j+4]+=g*w;C[j+5]+=b*w;j+=TW*3;w=(1-fx)*fy*k;C[j]+=r*w;C[j+1]+=g*w;C[j+2]+=b*w;w=fx*fy*k;C[j+3]+=r*w;C[j+4]+=g*w;C[j+5]+=b*w;}
function streak(x0,y0,x1,y1,wd,r,g,b,a){const p=wd/2+1,xa=Math.max(0,Math.floor(Math.min(x0,x1)-p)),xb=Math.min(TW-1,Math.ceil(Math.max(x0,x1)+p)),ya=Math.max(0,Math.floor(Math.min(y0,y1)-p)),yb=Math.min(TH-1,Math.ceil(Math.max(y0,y1)+p)),dx=x1-x0,dy=y1-y0,l2=dx*dx+dy*dy||1e-9;
 for(let y=ya;y<=yb;y++)for(let x=xa;x<=xb;x++){const px=x+.5-x0,py=y+.5-y0,u=clamp((px*dx+py*dy)/l2,0,1),cov=clamp(wd/2-Math.hypot(px-u*dx,py-u*dy)+.5,0,1);if(cov>0){const j=(y*TW+x)*3,w=cov*a;C[j]+=r*w;C[j+1]+=g*w;C[j+2]+=b*w;}}}

// A six-sided gem (two pyramids base to base), flat-shaded per facet with a sharp highlight.
// G is the look: light (lx,ly,lz), ramp d/m/l, fog colour and amount k, opacity and blend mode.
const GEM={lx:.38,ly:.8,lz:-.46,d:[0,0,0],m:[0,0,0],l:[0,0,0],fog:[0,0,0],k:0,al:1,mode:0,spec:380};
function drawGem(X,Y,Z,rad,hh,spin,flip){const G=GEM;let vx=cam.px-X,vy=cam.py-Y*flip,vz=cam.pz-Z;const vl=1/Math.hypot(vx,vy,vz);let hx=G.lx+vx*vl,hy=G.ly+vy*vl,hz=G.lz+vz*vl;const hl=1/Math.hypot(hx,hy,hz);hx*=hl;hy*=hl;hz*=hl;
 for(let q=0;q<6;q++){const a=spin+q*TAU/6;vtx(q,X+Math.cos(a)*rad,Y,Z+Math.sin(a)*rad);}vtx(6,X,Y+hh,Z);vtx(7,X,Y-hh,Z);
 const ap=rad*.866,nl=1/Math.hypot(hh,ap),nhz=hh*nl,nv=ap*nl,k=G.k;
 for(let q=0;q<6;q++){const am=spin+(q+.5)*TAU/6,nx=nhz*Math.cos(am),nz=nhz*Math.sin(am);
  for(let s=1;s>=-1;s-=2){const ny=nv*s*flip,dd=nx*G.lx+ny*G.ly+nz*G.lz,nh=nx*hx+ny*hy+nz*hz,sp=nh>0?Math.pow(nh,26)*G.spec:0,q1=clamp(.24+.76*(dd>0?dd:-dd*.3),0,1);
   const a0=q1<.5?G.d:G.m,b0=q1<.5?G.m:G.l,u=q1<.5?q1*2:q1*2-1;let r=a0[0]+(b0[0]-a0[0])*u+sp,g=a0[1]+(b0[1]-a0[1])*u+sp,b=a0[2]+(b0[2]-a0[2])*u+sp;r+=(G.fog[0]-r)*k;g+=(G.fog[1]-g)*k;b+=(G.fog[2]-b)*k;
   trf(s>0?6:7,q,(q+1)%6,r,g,b,G.al,G.mode);}}}

// =====================================================================
// 1. Save point: the motes, grown up. A pillar of light stands on a dark glossy floor inside a
// slowly turning marker ring, and sparks spiral up through it, brightest in the core.
// =====================================================================
const savePoint=(()=>{
 const N=760,BIG=26,A=new Float32Array(N*6);
 for(let i=0;i<N;i++){const h=hash(i*1.31+.7);A[i*6]=.16+2.9*h*h;A[i*6+1]=hash(i*2.17+3)*TAU;A[i*6+2]=hash(i*3.71+5)*7.6;A[i*6+3]=.3+.7*hash(i*5.3+1);A[i*6+4]=i<BIG?.07+.08*hash(i*7.7+2):.016+.03*hash(i*9.1+4);A[i*6+5]=hash(i*11.3+6)*TAU;}
 return (ctx,t,c)=>{use(target(480,270));const P=tones(c),W=TW,H=TH,f=300,ox=240,oy=186,pitch=.2,camY=2.7;camYP(0,camY,0,0,pitch,f,ox,oy);
  const bx=2.6,bz=8.6;proj(bx,0,bz);const xb=SX,yb=SY,hz=Math.round(oy-f*Math.tan(pitch)),top=P.sh(.74),hor=P.sh(.3),lc=P.ti(.6),f0=P.sh(.78),cc=P.c;
  for(let y=0;y<H;y++){let j=y*W*3;
   if(y<hz){const g=y/hz,g2=g*g,br=lerp(top[0],hor[0],g2),bg=lerp(top[1],hor[1],g2),bb=lerp(top[2],hor[2],g2),hv=(y-yb)/230,hv2=hv*hv;
    for(let x=0;x<W;x++,j+=3){const hu=(x+.5-xb)/150,hl=.42/(1+hu*hu+hv2);C[j]=br+cc[0]*hl;C[j+1]=bg+cc[1]*hl;C[j+2]=bb+cc[2]*hl;}}
   else{if(!planeRow(y,0)){for(let x=0;x<W;x++,j+=3){C[j]=hor[0];C[j+1]=hor[1];C[j+2]=hor[2];}continue;}
    // the floor is glossy: toward the horizon it mirrors the glow above it, nearer it shows its own dark
    const e=1-(y-hz)/(H-hz),e3=e*e*e,br=lerp(f0[0],hor[0],e3),bg=lerp(f0[1],hor[1],e3),bb=lerp(f0[2],hor[2],e3),Z=RZ0,ddz=Z-bz,foot=RTH*RTH/(f*camY),hv=(2*hz-y-yb)/230,hv2=hv*hv,
     sw=5+(y-yb)*.5,sk=y>yb-3?.5*Math.pow(clamp(1-(y-yb)/(H-yb),0,1),1.4)*smooth(yb-3,yb+5,y):0,zc=Math.floor(Z*1.1),ckk=.05*clamp(9/RTH,0,1);
    for(let x=0;x<W;x++,j+=3){const X=RX0+x*RXS,ddx=X-bx,rr=ddx*ddx+ddz*ddz,hu=(x+.5-xb)/150,hl=.42*e3/(1+hu*hu+hv2);let a=.36/(1+rr*.4);
     if(rr<5.2){const r=Math.sqrt(rr),ew=Math.max(.05,foot*Math.abs(ddz)/(r+.01));a+=.26*smooth(1.2,.1,r);
      const b1=smooth(1.32-ew,1.32+ew,r)*smooth(1.66+ew,1.66-ew,r);if(b1>.01)a+=b1*.6*smooth(-.35,.35,Math.sin(Math.atan2(ddz,ddx)*8+t*.3));
      const b2=smooth(1.94-ew,1.94+ew,r)*smooth(2.05+ew,2.05-ew,r);if(b2>.01)a+=b2*.3*(.6+.4*Math.sin(Math.atan2(ddz,ddx)*3-t*.4));}
     if(sk>0){const u=(x+.5-xb)/sw,g1=1/(1+u*u);a+=sk*g1*g1;}
     const ck=((Math.floor(X*1.1+64)+zc)&1)?1:1-ckk;C[j]=br*ck+lc[0]*a+cc[0]*hl;C[j+1]=bg*ck+lc[1]*a+cc[1]*hl;C[j+2]=bb*ck+lc[2]*a+cc[2]*hl;}}}
  // the pillar: soft, a little wider at the top, with slow filaments climbing it
  for(let y=0;y<yb+2&&y<H;y++){const g=y/yb,I=(.14+.34*g*g)*smooth(yb+2,yb-8,y),wc=11+(yb-y)*.07,ph=1.8*Math.sin(y*.031+t*.55)+t*.3,xa=Math.max(0,Math.floor(xb-wc*6)),xe=Math.min(W-1,Math.ceil(xb+wc*6));
   for(let x=xa,j=(y*W+xa)*3;x<=xe;x++,j+=3){const dx=x+.5-xb,u=dx/wc,g1=1/(1+u*u),k=I*(g1*g1*(.75+.25*Math.sin(dx*.42+ph))+.6*g1*g1*g1*g1);C[j]+=lc[0]*k;C[j+1]+=lc[1]*k;C[j+2]+=lc[2]*k;}}
  for(let i=0;i<N;i++){const q=i*6,r0=A[q],y=(A[q+2]+A[q+3]*t*1.1)%7.6,rad=r0*(.5+.1*y)+.1*Math.sin(t*.6+i),om=.75/(.35+r0),ang=A[q+1]+t*om+y*.5,ca=Math.cos(ang),sa=Math.sin(ang),X=bx+rad*ca,Z=bz+rad*sa;
   if(!proj(X,y,Z))continue;const sc=f/SZ,px=SX,py=SY,k=smooth(0,.5,y)*smooth(7.6,5.4,y)*(.2+.8/(1+rad*rad*1.4))*(.75+.25*Math.sin(t*1.7+A[q+5])),size=A[q+4]*sc;
   if(i<BIG){glow(px,py,size*3.2,lc[0],lc[1],lc[2],k*.3);glow(px,py,size,lc[0],lc[1],lc[2],k*.55);}
   else{dot(px,py,lc[0],lc[1],lc[2],k*(.4+size*.36));streak(px,py,px+rad*sa*om*sc*.14,py+A[q+3]*1.1*sc*.14,1.1,lc[0],lc[1],lc[2],k*.2);}
   if(y<3.2&&proj(X,-y,Z)){const kr=k*.28*(1-y/3.2);if(i<BIG)glow(SX,SY,size*2.4,lc[0],lc[1],lc[2],kr);else dot(SX,SY,lc[0],lc[1],lc[2],kr);}}
  present(ctx,.22,165,.5,false);};
})();

// =====================================================================
// 2. Loose pixels: the motes made physical. Each mote is a small square chip, tumbling as it is
// carried up a winding current from the near right into the distance. Shafts of light cross the
// frame from the upper right; a chip lights up inside a shaft and flashes when it faces the light.
// =====================================================================
const loosePixels=(()=>{
 const N=760,DUST=220,A=new Float32Array(N*10),SRX=560,SRY=-110,LN=2048,lut=new Float32Array(LN),M=new Array(9),o=[0,0,0];let ang=null,fall=null;
 for(let i=0;i<N;i++){const q=i*10,h=hash(i*7.7+2);let ax=hash(i*5.1+1)-.5,ay=hash(i*6.3+8)-.5,az=hash(i*8.9+3)-.5;const l=Math.hypot(ax,ay,az)||1;
  A[q]=hash(i*1.31+.7);A[q+1]=.75+.55*hash(i*2.17+3);A[q+2]=Math.sqrt(hash(i*3.71+5));A[q+3]=hash(i*4.3+9)*TAU;A[q+4]=.045+.12*h*h*h;A[q+5]=ax/l;A[q+6]=ay/l;A[q+7]=az/l;A[q+8]=(.9+2.2*hash(i*9.7+4))*(i&1?1:-1);A[q+9]=hash(i*11.3+6)*TAU;}
 // the current, given as screen positions and depths and turned into points in space
 const K=[[590,350,3.4],[452,236,4.8],[300,186,7],[226,118,10.5],[300,60,15],[404,44,22],[486,18,30]].map(([sx,sy,z])=>[(sx-240)*z/300,(135-sy)*z/300,z]);
 const pathAt=s=>{const n=K.length-1,u=clamp(s,0,.9999)*n,i=Math.floor(u),f=u-i,p0=K[Math.max(0,i-1)],p1=K[i],p2=K[i+1],p3=K[Math.min(n,i+2)];
  for(let k=0;k<3;k++)o[k]=.5*(2*p1[k]+(p2[k]-p0[k])*f+(2*p0[k]-5*p1[k]+4*p2[k]-p3[k])*f*f+(3*p1[k]-p0[k]-3*p2[k]+p3[k])*f*f*f);};
 return (ctx,t,c)=>{use(target(480,270));const P=tones(c),W=TW,H=TH;camYP(0,0,0,0,0,300,240,135);
  if(!ang){ang=new Uint16Array(T.N);fall=new Float32Array(T.N);for(let y=0,i=0;y<H;y++)for(let x=0;x<W;x++,i++){const dx=x+.5-SRX,dy=y+.5-SRY;ang[i]=Math.floor(fract(Math.atan2(dy,dx)/TAU+1)*LN)%LN;fall[i]=1/(1+(dx*dx+dy*dy)/(430*430));}}
  // four shafts, each breathing and leaning a little
  lut.fill(0);for(let k=0;k<4;k++){const th=1.93+k*.245+.045*Math.sin(t*.13+k*2.1),w=.04+.035*hash(k+3),amp=(.5+.5*hash(k+9))*(.78+.22*Math.sin(t*.23+k*1.7)),i0=Math.floor((th-3*w)/TAU*LN),i1=Math.ceil((th+3*w)/TAU*LN);
   for(let i=i0;i<=i1;i++){const d=(i/LN*TAU-th)/w;lut[i&(LN-1)]+=amp*Math.exp(-d*d);}}
  const d0=P.sh(.7),d1=P.sh(.2),lc=P.ti(.5);
  for(let y=0,i=0,j=0;y<H;y++){const gy=(1-y/H)*.45;for(let x=0;x<W;x++,i++,j+=3){let g=x/W*.55+gy;g*=g;const b=lut[ang[i]]*fall[i]*.5;C[j]=d0[0]+(d1[0]-d0[0])*g+lc[0]*b;C[j+1]=d0[1]+(d1[1]-d0[1])*g+lc[1]*b;C[j+2]=d0[2]+(d1[2]-d0[2])*g+lc[2]*b;}}
  const lx=.42,ly=.74,lz=-.52,hl=Math.hypot(lx,ly,lz-1),hx=lx/hl,hy=ly/hl,hz=(lz-1)/hl,cd=P.sh(.52),cm=P.sh(.1),cl=P.ti(.55),fg=P.sh(.44);
  for(let i=0;i<N;i++){const q=i*10,s=fract(A[q]+t*.052*A[q+1]);pathAt(s);
   const R=.4+1.5*4*s*(1-s)+(s<.25?(.25-s)*2:0),rho=R*A[q+2],th=A[q+3]+t*.32*(A[q+1]-.55),x=o[0]+rho*Math.cos(th),y=o[1]+rho*Math.sin(th)*.8,z=o[2]+rho*Math.cos(th*2.3+1)*1.3;
   if(z<1.6||!proj(x,y,z))continue;const px=SX,py=SY,xi=clamp(px|0,0,W-1),yi=clamp(py|0,0,H-1),pi=yi*W+xi,beam=Math.min(1.15,lut[ang[pi]]*fall[pi]*1.6);
   rotAxis(A[q+5],A[q+6],A[q+7],t*A[q+8]*.55+A[q+9],M);const h=A[q+4]*smooth(0,.05,s)*smooth(1,.88,s)*clamp(z/5.5,.45,1);
   const nl=Math.abs(M[2]*lx+M[5]*ly+M[8]*lz),nh=Math.abs(M[2]*hx+M[5]*hy+M[8]*hz),sp=Math.pow(nh,40)*(.15+.85*beam)*170*clamp(z/7,.25,1),br=clamp(nl*(.36+.95*beam)+.08,0,1),k=clamp((z-12)/22,0,.8),a0=br<.5?cd:cm,b0=br<.5?cm:cl,u=br<.5?br*2:br*2-1;
   let r=a0[0]+(b0[0]-a0[0])*u+sp,g=a0[1]+(b0[1]-a0[1])*u+sp,b=a0[2]+(b0[2]-a0[2])*u+sp;r+=(fg[0]-r)*k;g+=(fg[1]-g)*k;b+=(fg[2]-b)*k;
   const ax=M[0]*h,ay=M[3]*h,az=M[6]*h,bx=M[1]*h,by=M[4]*h,bz=M[7]*h;
   vtx(0,x-ax-bx,y-ay-by,z-az-bz);vtx(1,x+ax-bx,y+ay-by,z+az-bz);vtx(2,x+ax+bx,y+ay+by,z+az+bz);vtx(3,x-ax+bx,y-ay+by,z-az+bz);
   // one corner a little brighter than the other, so a chip reads as a lit sheet and not a flat patch
   vcol(0,r*.86,g*.86,b*.86);vcol(1,r,g,b);vcol(2,r*1.14,g*1.14,b*1.14);vcol(3,r,g,b);tr(0,1,2,1,0);tr(0,2,3,1,0);}
  // fine dust in the same current
  for(let i=0;i<DUST;i++){const s=fract(hash(i*1.7+40)+t*.04*(.7+.6*hash(i*2.9+41)));pathAt(s);const rho=(.6+2.4*4*s*(1-s))*hash(i*3.3+42),th=hash(i*4.1+43)*TAU+t*.2;
   if(!proj(o[0]+rho*Math.cos(th),o[1]+rho*Math.sin(th),o[2]+rho*Math.sin(th*1.7)))continue;const xi=clamp(SX|0,0,W-1),yi=clamp(SY|0,0,H-1),pi=yi*W+xi,k=(.12+.9*Math.min(1,lut[ang[pi]]*fall[pi]*1.5))*smooth(0,.06,s)*smooth(1,.85,s)*clamp(6/SZ,.25,1);dot(SX,SY,lc[0],lc[1],lc[2],k*.7);}
  present(ctx,.26,170,.55);};
})();

// =====================================================================
// 3. Lift-off: the motes at the size of blocks. A floor of blocks runs to a hazy horizon; under a
// spotlight they come loose one at a time and float up into the light, turning, each leaving a
// soft shadow that fades as it climbs, while a new block rises to fill the gap. Fine dust climbs
// the beam with them.
// =====================================================================
const liftOff=(()=>{
 const S=1.25,HS=.56,IN=.47,M=new Array(9),fly=[];
 const CORN=[[-1,-1,-1],[1,-1,-1],[1,1,-1],[-1,1,-1],[-1,-1,1],[1,-1,1],[1,1,1],[-1,1,1]],FACE=[[0,-1,0,3,7,4],[0,1,1,2,6,5],[1,-1,0,1,5,4],[1,1,3,2,6,7],[2,-1,0,1,2,3],[2,1,4,5,6,7]];
 const cell=(i,j,t,o)=>{const X=i*S,Z=.2+j*S,d=Math.hypot(X-2.5,Z-8.6);o[0]=0;if(d<3.5&&hash2(i+31,j+17)<.56){const per=11+9*hash2(i+7,j+3),u=fract(t/per+hash2(i+1,j+9));o[0]=u<.7?1:2;o[1]=u<.7?u/.7:(u-.7)/.3;}};
 const cs=[0,0];
 return (ctx,t,c)=>{use(target(480,270));const P=tones(c),W=TW,H=TH;camYP(0,3.7,-1.8,0,.29,310,240,122);
  const sx0=2.5,sz0=8.6,cD=P.sh(.88),cM=P.sh(.4),cL=P.ti(.36),fg=P.sh(.12),lx=.32,ly=.86,lz=-.4,sky=P.sh(.5),hzy=Math.round(cam.oy-cam.f*Math.tan(.29));
  for(let y=0,j=0;y<H;y++){const g=clamp(y/hzy,0,1),g2=g*g,r=lerp(sky[0],fg[0],g2),gg=lerp(sky[1],fg[1],g2),b=lerp(sky[2],fg[2],g2);for(let x=0;x<W;x++,j+=3){C[j]=r;C[j+1]=gg;C[j+2]=b;}}
  const ramp=(I,z)=>{I=I<0?0:I>1.3?1.3:I;const a=I<.5?cD:cM,b=I<.5?cM:cL,u=I<.5?I*2:(I-.5)*1.25,k=Math.pow(clamp((z-8)/20,0,1),1.4);R_=a[0]+(b[0]-a[0])*u;G_=a[1]+(b[1]-a[1])*u;B_=a[2]+(b[2]-a[2])*u;R_+=(fg[0]-R_)*k;G_+=(fg[1]-G_)*k;B_+=(fg[2]-B_)*k;};
  fly.length=0;for(let j=3;j<11;j++)for(let i=-1;i<=5;i++){cell(i,j,t,cs);if(cs[0]===1)fly.push(i*S,.2+j*S,cs[1],hash2(i,j));}
  // dark under everything, so a gap is a hole
  const un=P.sh(.96);vtx(0,-40,-1.6,-1);vtx(1,40,-1.6,-1);vtx(2,40,-1.6,34);vtx(3,-40,-1.6,34);qf(0,1,2,3,un[0],un[1],un[2],1,0);
  for(let j=0;j<27;j++){const Z=.2+j*S;for(let i=-16;i<=16;i++){const X=i*S;if(Math.abs(X)>Z*.82+2.6)continue;cell(i,j,t,cs);if(cs[0]===1)continue;
   const h=hash2(i,j),d=Math.hypot(X-sx0,Z-sz0),spot=1/(1+Math.pow(d/2.8,4));let y0=(h-.5)*.2+.05*Math.sin(t*.5+i*.7+j*.45),I=.2+1.02*spot+(h-.5)*.08;
   if(cs[0]===2){y0-=1.3*(1-smooth(0,1,cs[1]));I*=.5+.5*cs[1];}
   for(let k=0;k<fly.length;k+=4){const dx=X-fly[k],dz=Z-fly[k+1],q=dx*dx+dz*dz;if(q<4)I*=1-.5*(1-fly[k+2])*smooth(1.9,.4,Math.sqrt(q));}
   const x0=X-HS,x1=X+HS,z0=Z-HS,z1=Z+HS,yb=y0-2*HS;vtx(0,x0,y0,z0);vtx(1,x1,y0,z0);vtx(2,x1,y0,z1);vtx(3,x0,y0,z1);vtx(4,x0,yb,z0);vtx(5,x1,yb,z0);const zv=SZ;
   // the top has a paler worn edge round a flat face
   ramp(I*1.22+.05,zv);qf(0,1,2,3,R_,G_,B_,1,0);
   if(zv<19){vtx(7,X-IN,y0+.004,Z-IN);vtx(8,X+IN,y0+.004,Z-IN);vtx(9,X+IN,y0+.004,Z+IN);vtx(10,X-IN,y0+.004,Z+IN);ramp(I,zv);qf(7,8,9,10,R_,G_,B_,1,0);}
   ramp(I*.5,zv);const fr=R_,fgn=G_,fb=B_;ramp(I*.3,zv);tri(VX[0],VY[0],VW[0],fr,fgn,fb,VX[1],VY[1],VW[1],fr,fgn,fb,VX[5],VY[5],VW[5],R_,G_,B_,1,0);tri(VX[0],VY[0],VW[0],fr,fgn,fb,VX[5],VY[5],VW[5],R_,G_,B_,VX[4],VY[4],VW[4],R_,G_,B_,1,0);
   if(X>cam.px){vtx(6,x0,yb,z1);ramp(I*.2,zv);qf(0,3,6,4,R_,G_,B_,1,0);}else{vtx(6,x1,yb,z1);ramp(I*.4,zv);qf(1,2,6,5,R_,G_,B_,1,0);}}}
  // the beam, wider at the floor
  proj(sx0,0,sz0);const xs=SX,ys=SY,lc=P.ti(.55);
  for(let y=0;y<H&&y<ys+40;y++){const fr=(y+50)/(ys+50),xc=xs+70*(1-fr),w=24+62*fr,I=.3*(.45+.55*fr)*smooth(ys+40,ys-30,y),xa=Math.max(0,Math.floor(xc-w*2.6)),xe=Math.min(W-1,Math.ceil(xc+w*2.6));
   for(let x=xa,j=(y*W+xa)*3;x<=xe;x++,j+=3){const u=(x+.5-xc)/w,u2=u*u,g=I/(1+u2*u2);C[j]+=lc[0]*g;C[j+1]+=lc[1]*g;C[j+2]+=lc[2]*g;}}
  // the blocks in the air, lit from above and brighter the higher they are
  for(let k=0;k<fly.length;k+=4){const X=fly[k],Z=fly[k+1],v=fly[k+2],h=fly[k+3],lift=7.4*Math.pow(v,1.7),cy=-HS+lift,a=Math.pow(v,1.5)*(2.5+4.5*h),al=smooth(1,.8,v),hs=HS*(1-.1*v);
   let ax=h-.5,ay=.35,az=hash(h*91.7)-.5;const l=Math.hypot(ax,ay,az);rotAxis(ax/l,ay/l,az/l,a,M);
   for(let q=0;q<8;q++){const cx=CORN[q][0]*hs,cyy=CORN[q][1]*hs,cz=CORN[q][2]*hs;vtx(q,X+M[0]*cx+M[1]*cyy+M[2]*cz,cy+M[3]*cx+M[4]*cyy+M[5]*cz,Z+M[6]*cx+M[7]*cyy+M[8]*cz);}
   if(!proj(X,cy,Z))continue;const zv=SZ,up=.75+.5*v;glow(SX,SY,Math.min(46,2.4*cam.f/zv),lc[0],lc[1],lc[2],.1*al);
   for(const F of FACE){const nx=M[F[0]]*F[1],ny=M[3+F[0]]*F[1],nz=M[6+F[0]]*F[1];if((cam.px-X-nx*hs)*nx+(cam.py-cy-ny*hs)*ny+(cam.pz-Z-nz*hs)*nz<=0)continue;
    const dd=nx*lx+ny*ly+nz*lz;ramp((.26+.86*(dd>0?dd:0))*up,zv);qf(F[2],F[3],F[4],F[5],R_,G_,B_,al,al<1?1:0);}}
  for(let i=0;i<110;i++){const hg=fract(hash(i*1.9+60)+t*.05*(.5+hash(i*2.3+61))),a=hash(i*3.1+62)*TAU+t*.25*(hash(i*4.7+63)-.5),r=2.6*Math.sqrt(hash(i*5.3+64))*(.45+.55*(1-hg));
   if(!proj(sx0+r*Math.cos(a)+1.7*hg,hg*8.2,sz0+r*Math.sin(a)))continue;const k=smooth(0,.08,hg)*smooth(1,.8,hg)*(.6+.4*Math.sin(t*1.3+i*2.1));if(hash(i*7.1)>.78)glow(SX,SY,3.4,lc[0],lc[1],lc[2],k*.55);else dot(SX,SY,lc[0],lc[1],lc[2],k*.8);}
  present(ctx,.24,175,.5);};
})();

// =====================================================================
// 4. Pickup trail: a slow flight along a winding line of gems. Each hovers over a glossy tiled
// floor, bobbing and spinning in a wave that travels down the line, mirrored beneath itself, and
// sweeps past on the right as you reach it.
// =====================================================================
const pickupTrail=(()=>{
 const G=2.5,NG=30,gx=new Float32Array(NG);
 const trX=Z=>2.05+1.05*Math.sin(Z*.085)+.45*Math.sin(Z*.039+1.3);
 return (ctx,t,c)=>{use(target(480,270));const P=tones(c),W=TW,H=TH,f=300,pitch=.07;
  const Zc=t*2.3,camx=.5*(1.05*Math.sin(Zc*.085-.25)+.45*Math.sin(Zc*.039+1.05)),yaw=.1*Math.sin(Zc*.085+.7)+.07,camY=1.32+.05*Math.sin(t*.7);
  camYP(camx,camY,Zc,yaw,pitch,f,222,116);
  const hz=cam.oy-f*Math.tan(pitch),top=P.sh(.74),hc=P.sh(.06),fa=P.sh(.66),fb=P.sh(.54),lc=P.ti(.55),cD=P.sh(.6),cM=P.ti(.04),cL=P.ti(.62);
  proj(trX(Zc+70),1,Zc+70);const xv=SX,gi0=Math.ceil((Zc+.9)/G);for(let k=0;k<NG;k++)gx[k]=trX((gi0+k)*G);
  for(let y=0;y<H;y++){let j=y*W*3;
   if(y<hz||!planeRow(y,0)){const g=clamp(y/hz,0,1),g2=g*g,br=lerp(top[0],hc[0],g2),bg=lerp(top[1],hc[1],g2),bb=lerp(top[2],hc[2],g2),v=(y-hz)/46,v2=v*v;
    for(let x=0;x<W;x++,j+=3){const u=(x+.5-xv)/110,gl=.36/(1+u*u+v2);C[j]=br+lc[0]*gl;C[j+1]=bg+lc[1]*gl;C[j+2]=bb+lc[2]*gl;}continue;}
   const fk=Math.pow(clamp((RTH-2.5)/34,0,1),.75),v=(y-hz)/30,v2=v*v;
   for(let x=0;x<W;x++,j+=3){const X=RX0+x*RXS,Z=RZ0+x*RZS,zi=Math.floor(Z/G+.5),k=zi-gi0,base=((Math.floor(X/G+.5)+zi)&1)?fa:fb;let a=0;
    if(k>=0&&k<NG){const dx=X-gx[k],dz=Z-zi*G;a=.5/(1+(dx*dx+dz*dz)*2.6);}else if(k===-1){const dx=X-trX(zi*G),dz=Z-zi*G;a=.5/(1+(dx*dx+dz*dz)*2.6);}
    const u=(x+.5-xv)/110,gl=.24/(1+u*u+v2);
    C[j]=base[0]+lc[0]*a+(hc[0]-base[0]-lc[0]*a)*fk+lc[0]*gl;C[j+1]=base[1]+lc[1]*a+(hc[1]-base[1]-lc[1]*a)*fk+lc[1]*gl;C[j+2]=base[2]+lc[2]*a+(hc[2]-base[2]-lc[2]*a)*fk+lc[2]*gl;}}
  // mirrored first, faint, then the gems themselves, each with a soft light round it
  GEM.lx=.38;GEM.ly=.8;GEM.lz=-.46;GEM.d=cD;GEM.m=cM;GEM.l=cL;GEM.fog=hc;GEM.spec=300;
  const gem=(k,flip)=>{const gi=gi0+k,X=gx[k],Y=(1.02+.14*Math.sin(t*1.5-gi*.7))*flip,Z=gi*G;if(!proj(X,Y,Z))return;const zv=SZ,fk=Math.pow(clamp((zv-2.5)/34,0,1),.75);
   if(flip>0)glow(SX,SY,Math.min(34,1.1*f/zv),lc[0],lc[1],lc[2],.3*(1-fk));GEM.k=fk;GEM.spec=300*smooth(1.5,6,zv);GEM.al=flip>0?1:.3;GEM.mode=flip>0?0:5;drawGem(X,Y,Z,.31,.52,t*1.25+gi*.9,flip);};
  for(let k=NG-1;k>=0;k--)gem(k,-1);
  D.fill(0);for(let k=NG-1;k>=0;k--)gem(k,1);
  present(ctx,.25,175,.55);};
})();

// =====================================================================
// 5. Thumbstick: one large analogue stick in its well, softly lit from the right, being played
// by an unseen thumb: pushed over, rolled round, flicked, and let go to spring back and settle.
// =====================================================================
const thumbstick=(()=>{
 // the cap, turned on a lathe: rounded edge, raised rim, a band of fine grip rings, then a smooth shallow dish
 const prof=[[.3,-.2],[.62,-.19],[.9,-.15],[.985,-.07],[1,0],[.985,.07],[.945,.128],[.89,.16],[.84,.166]];
 for(let k=0;k<=12;k++){const r=.81-k*.019;prof.push([r,.158-.01*k/12+.011*Math.cos(k*Math.PI/2)]);}
 for(let k=1;k<=14;k++){const r=.56*(1-k/14);prof.push([r,.148-.07*(1-(r/.56)*(r/.56))]);}
 const capM=latheMesh(prof,56),stemM=latheMesh([[.54,-1.1],[.42,-.9],[.36,-.55],[.37,-.32],[.5,-.2]],28),domeM=sphereMesh(36,18),M=new Array(9);
 const spring=v=>Math.exp(-7*v)*Math.cos(19*v),eo=x=>1-(1-x)*(1-x)*(1-x);
 // where the thumb has the stick at time t, as a point in the unit disc
 const pose=(t,o)=>{const TC=7,n=Math.floor(t/TC),u=t-n*TC;
  if(n%3===2){const m=Math.floor(u/(TC/3)),w=u-m*TC/3,a=hash(n*7.3+m*13.1)*TAU,e=w<.12?eo(w/.12):w<.85?1:spring(w-.85);o[0]=Math.cos(a)*e;o[1]=Math.sin(a)*e;return;}
  const h=hash(n*3.1+1),uu=Math.min(u,5.6),a=hash(n*5.7+2)*TAU+(h>.5?1:-1)*(1.25*uu+.25*Math.sin(uu*1.1)),e=u<.3?eo(u/.3):u<5.6?.88+.12*Math.sin(u*1.7+h*6):spring(u-5.6);o[0]=Math.cos(a)*e;o[1]=Math.sin(a)*e;};
 const ps=[0,0];
 return (ctx,t,c)=>{use(target(480,270));const P=tones(c),W=TW,H=TH;camLook(-2.6,6.4,-6.6,.5,.35,0,720,348,138);
  pose(t,ps);const e=Math.hypot(ps[0],ps[1]),th=.36*e,dx=e>1e-4?ps[0]/e:1,dz=e>1e-4?ps[1]/e:0;rotAxis(dz,0,-dx,th,M);
  const ax=M[1],ay=M[4],az=M[7],ccx=ax*1.9,ccy=-1+ay*1.9,ccz=az*1.9,lx=.62,ly=.74,lz=-.26,la=lx*ax+ly*ay+lz*az;
  // how far a point sits inside the shadow of the cap (1 = fully)
  const shadow=(x,y,z)=>{const s=((ccx-x)*ax+(ccy-y)*ay+(ccz-z)*az)/la;if(s<=0)return 0;const qx=x+lx*s-ccx,qy=y+ly*s-ccy,qz=z+lz*s-ccz;return smooth(1.16,.84,Math.sqrt(qx*qx+qy*qy+qz*qz));};
  const sA=P.sh(.86),sB=P.sh(.1),wl=P.sh(.94),wf=P.sh(.62),lc=P.ti(.35),fl=Math.hypot(cam.fx,cam.fz),fxh=cam.fx/fl,fzh=cam.fz/fl;
  for(let y=0;y<H;y++){let i=y*W,j=i*3;if(!planeRow(y,0)){for(let x=0;x<W;x++,j+=3){C[j]=sA[0];C[j+1]=sA[1];C[j+2]=sA[2];}continue;}const wv=1/RTH;
   for(let x=0;x<W;x++,i++,j+=3){const X=RX0+x*RXS,Z=RZ0+x*RZS,r2=X*X+Z*Z;
    if(r2<2.25){const tv=smooth(-.1,1.25,(X*fxh+Z*fzh)/1.5);C[j]=wl[0]+(wf[0]-wl[0])*tv;C[j+1]=wl[1]+(wf[1]-wl[1])*tv;C[j+2]=wl[2]+(wf[2]-wl[2])*tv;continue;}
    D[i]=wv;const px=X-1.8,pz=Z-.3;let lp=1/(1+(px*px+pz*pz)*.05),add=0;
    if(r2<3.24){const r=Math.sqrt(r2),u=(r-1.5)/.3;add=-.36*Math.cos(Math.PI*u)*(X*lx+Z*lz)/r+.1*Math.sin(Math.PI*u);}
    else{if(r2<7.3){const r=Math.sqrt(r2),ew=RTH*.0022;lp*=1-.2*smooth(2.7,1.8,r);add=.07*smooth(ew,0,Math.abs(r-2.34))-.1*smooth(ew,0,Math.abs(r-2.3));}lp*=1-.62*shadow(X,0,Z);}   // a fine engraved ring round the well
    C[j]=sA[0]+(sB[0]-sA[0])*lp+lc[0]*add;C[j+1]=sA[1]+(sB[1]-sA[1])*lp+lc[1]*add;C[j+2]=sA[2]+(sB[2]-sA[2])*lp+lc[2]*add;}}
  MT.lx=lx;MT.ly=ly;MT.lz=lz;MT.amb=.24;MT.fn=0;MT.fr=1e9;MT.rc=P.ti(.45);
  // the dome under the stick, cut off at the depth of the well
  MT.d=P.sh(.95);MT.m=P.sh(.62);MT.l=P.sh(.22);MT.spec=.12;MT.shin=18;MT.rim=.1;
  drawMesh(domeM,IDENT,1.22,0,-1,0,(wx,wy,wz,nx,ny,nz,v)=>{if(wy<-.42){VOK[v]=0;return;}MT.k=1-.62*shadow(wx,wy,wz);lit(wx,wy,wz,nx,ny,nz);},1,0);
  MT.d=P.sh(.9);MT.m=P.sh(.5);MT.l=P.sh(.1);MT.spec=.15;MT.shin=20;MT.rim=.16;MT.k=.6;drawMesh(stemM,M,1,ccx,ccy,ccz,lit,1,0);
  MT.d=P.sh(.8);MT.m=P.sh(.12);MT.l=P.ti(.36);MT.spec=.3;MT.shin=12;MT.rim=.42;MT.k=1;drawMesh(capM,M,1,ccx,ccy,ccz,lit,1,0);
  present(ctx,.2,185,.4);};
})();

// =====================================================================
// 6. Button field: a wide dark panel of round domed buttons running to the horizon. Waves of
// presses cross it: each button goes down, the gap around it lights from within, and it springs
// back with a little overshoot, as if a great many unseen thumbs were playing.
// =====================================================================
const buttonField=(()=>{
 const Q=3.7,GN=44,OFF=13,press=new Float32Array(GN*GN),lx=.46,ly=.7,lz=.55,
  LOD=[latheMesh([[.8,0],[.8,.3],[.775,.36],[.7,.41],[.54,.455],[.3,.488],[0,.5]],26),latheMesh([[.8,0],[.8,.3],[.72,.4],[.45,.475],[0,.5]],16),latheMesh([[.8,0],[.8,.31],[.56,.455],[0,.5]],10)];
 const pulse=x=>x<.035?1-(1-x/.035)*(1-x/.035):x<.13?1:x<.45?Math.exp(-5*(x-.13)/.32)*Math.cos(11*(x-.13)/.32):0;
 return (ctx,t,c)=>{use(target(480,270));const P=tones(c),W=TW,H=TH;camYP(1.5+.5*Math.sin(t*.07),5.6,-3,.06,.46,380,240,80);
  for(let a=0;a<GN;a++)for(let b=0;b<GN;b++){const iu=a-OFF,iv=b-OFF,X=(iu-iv)*Q/2,Z=(iu+iv)*Q/2,h=hash2(iu,iv);
   press[a*GN+b]=Math.max(pulse(fract((X*.8+Z*.6)*.12-t*.17+.03*h)),pulse(fract((Z*.87-X*.5)*.08-t*.11+.5+.03*h)),pulse(fract(h*7.3+t/(9+8*h)))*.9);}
  const pa=P.sh(.86),pb=P.sh(.5),hz=P.sh(.04),lc=P.ti(.5),gap=P.sh(.95),cD=P.sh(.84),cM=P.sh(.4),cL=P.ti(.1);
  const fogk=z=>{const k=clamp((z-9)/34,0,1);return k*k*(3-2*k);};
  for(let y=0;y<H;y++){let j=y*W*3;if(!planeRow(y,0)){for(let x=0;x<W;x++,j+=3){C[j]=hz[0];C[j+1]=hz[1];C[j+2]=hz[2];}continue;}
   const fk=fogk(RTH);
   for(let x=0;x<W;x++,j+=3){const X=RX0+x*RXS,Z=RZ0+x*RZS,u=(X+Z)/Q,v=(Z-X)/Q,iu=Math.round(u),iv=Math.round(v),ddx=X-(iu-iv)*Q/2,ddz=Z-(iu+iv)*Q/2,r=Math.sqrt(ddx*ddx+ddz*ddz),a=iu+OFF,b=iv+OFF,p=a>=0&&b>=0&&a<GN&&b<GN?press[a*GN+b]:0,pp=p>0?p:0;
    let g=clamp(.25+X*.03+Z*.02,0,1),e=0,r0,g0,b0;
    if(r<.94){r0=gap[0];g0=gap[1];b0=gap[2];e=pp*(.25+.75*smooth(.7,.94,r));}
    else{g*=1-.55*smooth(1.45,.94,r);r0=pa[0]+(pb[0]-pa[0])*g;g0=pa[1]+(pb[1]-pa[1])*g;b0=pa[2]+(pb[2]-pa[2])*g;const q=(r-.94)/.16;e=pp*.75/(1+q*q*q*q);}
    r0+=lc[0]*e;g0+=lc[1]*e;b0+=lc[2]*e;C[j]=r0+(hz[0]-r0)*fk;C[j+1]=g0+(hz[1]-g0)*fk;C[j+2]=b0+(hz[2]-b0)*fk;}}
  for(let a=0;a<GN;a++)for(let b=0;b<GN;b++){const iu=a-OFF,iv=b-OFF,X=(iu-iv)*Q/2,Z=(iu+iv)*Q/2,rz=Z-cam.pz,rx=X-cam.px;if(rz<.5||rz>46||Math.abs(rx-rz*.06)>rz*.7+2.6)continue;
   const p=press[a*GN+b],Mh=LOD[rz<13?0:rz<24?1:2],Pp=Mh.P,Nn=Mh.N,I=Mh.I,drop=p*.24,pp=p>0?p:0;
   let vx=cam.px-X,vy=cam.py-.4,vz=cam.pz-Z;const vl=1/Math.hypot(vx,vy,vz);let hx=lx+vx*vl,hy=ly+vy*vl,hzz=lz+vz*vl;const hl=1/Math.hypot(hx,hy,hzz);hx*=hl;hy*=hl;hzz*=hl;
   for(let v=0,q=0;v<Mh.n;v++,q+=3){const ly0=Pp[q+1],wy=ly0>.01?ly0-drop*Math.min(1,ly0/.3):0;if(!vtx(v,X+Pp[q],wy,Z+Pp[q+2]))continue;
    const nx=Nn[q],ny=Nn[q+1],nz=Nn[q+2],dd=nx*lx+ny*ly+nz*lz,nh=nx*hx+ny*hy+nz*hzz,sp=nh>0?Math.pow(nh,22)*60:0,q1=clamp(.14+.86*(dd>0?dd:0),0,1),a0=q1<.5?cD:cM,b0=q1<.5?cM:cL,u=q1<.5?q1*2:q1*2-1,gl=pp*(.1+.22*(1-ny)),k=fogk(SZ);
    const r=a0[0]+(b0[0]-a0[0])*u+sp+lc[0]*gl,g=a0[1]+(b0[1]-a0[1])*u+sp+lc[1]*gl,bl=a0[2]+(b0[2]-a0[2])*u+sp+lc[2]*gl;VR[v]=r+(hz[0]-r)*k;VG[v]=g+(hz[1]-g)*k;VB[v]=bl+(hz[2]-bl)*k;}
   for(let k=0;k<I.length;k+=3)tr(I[k],I[k+1],I[k+2],1,0);}
  present(ctx,.3,180,.55);};
})();

// =====================================================================
// 7. Ball run: you follow a banded ball down a long checker tube that curves, rises and falls.
// The light is ahead: the far end glows and the floor of the tube catches it; a broken line of
// lamps runs along the roof and comes toward you dash by dash; and the ball rides up the wall on
// the outside of every bend.
// =====================================================================
const ballRun=(()=>{
 const NR=40,KR=62,DS=.8,RT=2.3,RB=.7,ballM=sphereMesh(30,16),F=new Float64Array(12),Fm=new Array(9),Ra=new Array(9),Rb=new Array(9),M=new Array(9),M2=new Array(9),cs=new Float32Array(NR),sn=new Float32Array(NR),gl=new Float32Array(NR),wash=new Float32Array(NR),fk=new Float32Array(KR),em=new Float32Array(KR);
 for(let j=0;j<NR;j++){const a=(j+.5)/NR*TAU;cs[j]=Math.cos(a);sn[j]=Math.sin(a);gl[j]=Math.pow(Math.max(0,-sn[j]),5)*.3;wash[j]=(j===9||j===10)?1.1:.2*Math.pow(Math.max(0,sn[j]),3)+.22*Math.pow(Math.max(0,-sn[j]),14);}
 const px=s=>7*Math.sin(s*.045)+2.5*Math.sin(s*.1+1),py=s=>1.5*Math.sin(s*.037+2),dxs=s=>.315*Math.cos(s*.045)+.25*Math.cos(s*.1+1),dys=s=>.0555*Math.cos(s*.037+2),ddx=s=>-.014175*Math.sin(s*.045)-.025*Math.sin(s*.1+1);
 // the tube at s: centre F[0..2], right F[3..5], up F[6..8], forward F[9..11]
 const frame=s=>{let tx=dxs(s),ty=dys(s),tz=1;const l=1/Math.hypot(tx,ty,tz);tx*=l;ty*=l;tz*=l;const rl=1/Math.hypot(tx,tz),rx=tz*rl,rz=-tx*rl;F[0]=px(s);F[1]=py(s);F[2]=s;F[3]=rx;F[4]=0;F[5]=rz;F[6]=ty*rz;F[7]=tz*rx-tx*rz;F[8]=-ty*rx;F[9]=tx;F[10]=ty;F[11]=tz;};
 // the roof lamps: lit for four rings, dark for four
 const lamp=n=>(((n%8)+8)%8)<4?1:0;
 return (ctx,t,c)=>{use(target(480,270));const P=tones(c),sc=t*3.1;
  frame(sc);const ex=F[0]-.6*F[3]+.3*F[6],ey=F[1]-.6*F[4]+.3*F[7],ez=F[2]-.6*F[5]+.3*F[8];frame(sc+7.5);camLook(ex,ey,ez,F[0],F[1]-.15,F[2],285,268,124);
  const exit=P.ti(.74),A=P.sh(.78),B=P.sh(.52),lc=P.ti(.6);fill(exit);
  const k0=Math.floor(sc/DS)-1;
  for(let k=0;k<KR;k++){const s=(k0+k)*DS;frame(s);for(let j=0;j<NR;j++)vtx(k*NR+j,F[0]+RT*(cs[j]*F[3]+sn[j]*F[6]),F[1]+RT*(cs[j]*F[4]+sn[j]*F[7]),F[2]+RT*(cs[j]*F[5]+sn[j]*F[8]));
   fk[k]=Math.pow(clamp((s-sc-2)/42,0,1),1.9);em[k]=lamp(k0+k)*smooth(1.5,8,s-sc);}
  for(let k=0;k<KR-1;k++){const par=(k0+k)>>1,f0=fk[k],f1=fk[k+1],e0=em[k];
   for(let j=0;j<NR;j++){const j2=(j+1)%NR,a=k*NR+j,b=k*NR+j2,cq=(k+1)*NR+j2,d=(k+1)*NR+j;if(!(VOK[a]&&VOK[b]&&VOK[cq]&&VOK[d]))continue;
    const bs=((par+(j>>2))&1)?A:B,wj=j===9||j===10?wash[j]:(wash[j]+wash[j2])*.5,ga=gl[j]+e0*wj,gb=gl[j2]+e0*wj,gc=gb,gd=ga;
    const ar=bs[0]+lc[0]*ga,ag=bs[1]+lc[1]*ga,ab=bs[2]+lc[2]*ga,br=bs[0]+lc[0]*gb,bg=bs[1]+lc[1]*gb,bb=bs[2]+lc[2]*gb,cr=bs[0]+lc[0]*gc,cg=bs[1]+lc[1]*gc,cb=bs[2]+lc[2]*gc,dr=bs[0]+lc[0]*gd,dg=bs[1]+lc[1]*gd,db=bs[2]+lc[2]*gd;
    tri(VX[a],VY[a],VW[a],ar+(exit[0]-ar)*f0,ag+(exit[1]-ag)*f0,ab+(exit[2]-ab)*f0,VX[b],VY[b],VW[b],br+(exit[0]-br)*f0,bg+(exit[1]-bg)*f0,bb+(exit[2]-bb)*f0,VX[cq],VY[cq],VW[cq],cr+(exit[0]-cr)*f1,cg+(exit[1]-cg)*f1,cb+(exit[2]-cb)*f1,1,0);
    tri(VX[a],VY[a],VW[a],ar+(exit[0]-ar)*f0,ag+(exit[1]-ag)*f0,ab+(exit[2]-ab)*f0,VX[cq],VY[cq],VW[cq],cr+(exit[0]-cr)*f1,cg+(exit[1]-cg)*f1,cb+(exit[2]-cb)*f1,VX[d],VY[d],VW[d],dr+(exit[0]-dr)*f1,dg+(exit[1]-dg)*f1,db+(exit[2]-db)*f1,1,0);}}
  // the ball
  const sb=sc+5.6+.5*Math.sin(t*.45),nb=Math.floor(sb/DS),lb=lamp(nb)*.8;frame(sb);const beta=clamp(-ddx(sb)*42,-1,1)+.1*Math.sin(t*1.1),sbt=Math.sin(beta),cbt=Math.cos(beta),d=RT-RB,
   ox=sbt*F[3]-cbt*F[6],oy=sbt*F[4]-cbt*F[7],oz=sbt*F[5]-cbt*F[8],bx=F[0]+d*ox,by=F[1]+d*oy,bz=F[2]+d*oz,e1x=cbt*F[3]+sbt*F[6],e1y=cbt*F[4]+sbt*F[7],e1z=cbt*F[5]+sbt*F[8];
  const sd=P.sh(.96),wx0=F[0]+(RT-.05)*ox,wy0=F[1]+(RT-.05)*oy,wz0=F[2]+(RT-.05)*oz;
  for(let n=0;n<6;n++){const rad=1.3-n*.16;vtx(0,wx0,wy0,wz0);for(let q=0;q<16;q++){const a=q/16*TAU,ca=Math.cos(a)*rad,sa=Math.sin(a)*rad*1.25;vtx(1+q,wx0+ca*e1x+sa*F[9],wy0+ca*e1y+sa*F[10],wz0+ca*e1z+sa*F[11]);}
   for(let q=0;q<16;q++)trf(0,1+q,1+(q+1)%16,sd[0],sd[1],sd[2],.13,4);}
  Fm[0]=F[3];Fm[1]=F[6];Fm[2]=F[9];Fm[3]=F[4];Fm[4]=F[7];Fm[5]=F[10];Fm[6]=F[5];Fm[7]=F[8];Fm[8]=F[11];rotAxis(1,0,0,sb/RB,Ra);rotAxis(0,0,1,.55,Rb);mmul(Fm,Ra,M2);mmul(M2,Rb,M);
  let lx=F[9]*.7+F[6]*.66+F[3]*.1,ly=F[10]*.7+F[7]*.66+F[4]*.1,lz=F[11]*.7+F[8]*.66+F[5]*.1;const ll=1/Math.hypot(lx,ly,lz);lx*=ll;ly*=ll;lz*=ll;
  const bd=P.sh(.25),bl=P.ti(.7),dk=P.sh(.82),BP=ballM.P;
  drawMesh(ballM,M,RB,bx,by,bz,(wx,wy,wz,nx,ny,nz,v)=>{const st=smooth(-.12,.12,Math.sin(BP[v*3+1]*4.2)),dd=nx*lx+ny*ly+nz*lz,q=Math.min(1,.4+.6*(dd>0?dd:0)+lb*.25);
   let vx=cam.px-wx,vy=cam.py-wy,vz=cam.pz-wz;const vl=1/Math.sqrt(vx*vx+vy*vy+vz*vz);vx*=vl;vy*=vl;vz*=vl;let ndv=nx*vx+ny*vy+nz*vz;if(ndv<0)ndv=0;
   const hx=lx+vx,hy=ly+vy,hz=lz+vz,nh=(nx*hx+ny*hy+nz*hz)/Math.sqrt(hx*hx+hy*hy+hz*hz),sp=nh>0?Math.pow(nh,36)*130:0,ri=(1-ndv)*(1-ndv)*(1-ndv)*(.7+lb*.4);
   R_=dk[0]+(bd[0]+(bl[0]-bd[0])*st-dk[0])*q+sp+lc[0]*ri;G_=dk[1]+(bd[1]+(bl[1]-bd[1])*st-dk[1])*q+sp+lc[1]*ri;B_=dk[2]+(bd[2]+(bl[2]-bd[2])*st-dk[2])*q+sp+lc[2]*ri;},1,0);
  present(ctx,.3,175,.55);};
})();

// =====================================================================
// 8. Side-scroller: a level sliding past, seen from the side through a long lens. Stepped terraces
// of blocks stand at five depths and move at five speeds: the nearest dark against the light, the
// farthest pale in the haze. Slabs hover over them, rising and sinking a little, and a gem turns
// over each one.
// =====================================================================
const sideScroller=(()=>{
 const LZ=[11,18,27,38,52],BASE=[-2.5,-1.5,-.2,1.6,3.9],AMP=[2.2,3,3.6,4.4,5.4],GAP=[.1,.2,.24,.28,.3],FK=[0,.22,.46,.68,.85],CW=1.5,Q=.75,DP=2.2,F=560;
 const hgt=(l,i)=>BASE[l]+Q*Math.round(AMP[l]*(.62*vnoise(i*.17+l*31.7)+.38*vnoise(i*.47+l*11.3)-.5));
 const gap=(l,i)=>vnoise(i*.11+l*7.1)<GAP[l];
 return (ctx,t,c)=>{use(target(480,270));const P=tones(c),W=TW,H=TH,camX=t*1.25,camY=.3*Math.sin(t*.21);camYP(camX,camY,0,0,.015,F,240,124);
  const top=P.sh(.52),hzc=P.ti(.26),low=P.sh(.1),lc=P.ti(.6),hy=118;
  for(let y=0,j=0;y<H;y++){const g=y<hy?y/hy:1,g2=g*g,d=y>hy?clamp((y-hy)/(H-hy),0,1):0,br=lerp(lerp(top[0],hzc[0],g2),low[0],d),bg=lerp(lerp(top[1],hzc[1],g2),low[1],d),bb=lerp(lerp(top[2],hzc[2],g2),low[2],d),v=(y-74)/110,v2=v*v;
   for(let x=0;x<W;x++,j+=3){const u=(x-368)/170,gl=.36/(1+u*u+v2);C[j]=br+lc[0]*gl;C[j+1]=bg+lc[1]*gl;C[j+2]=bb+lc[2]*gl;}}
  const cTop=P.sh(.36),cLip=P.sh(.08),cF1=P.sh(.78),cF2=P.sh(.72),cSideL=P.sh(.88),cSideR=P.sh(.52),cUnder=P.sh(.9);
  const put=(col,k,m)=>{R_=(col[0]*m)+(hzc[0]-col[0]*m)*k;G_=(col[1]*m)+(hzc[1]-col[1]*m)*k;B_=(col[2]*m)+(hzc[2]-col[2]*m)*k;};
  GEM.lx=.45;GEM.ly=.75;GEM.lz=-.48;GEM.d=P.sh(.4);GEM.m=P.ti(.16);GEM.l=P.ti(.75);GEM.fog=hzc;GEM.al=1;GEM.mode=0;GEM.spec=240;
  for(let l=4;l>=0;l--){const z0=LZ[l],z1=z0+DP,k=FK[l],hw=z1*.45+2,i0=Math.floor((camX-hw)/CW),i1=Math.ceil((camX+hw)/CW),bot=camY-z0*.28;
   for(let i=i0;i<=i1;i++){if(gap(l,i))continue;const h=hgt(l,i),x0=i*CW,x1=x0+CW;
    if(h<camY+.4){vtx(0,x0,h,z0);vtx(1,x1,h,z0);vtx(2,x1,h,z1);vtx(3,x0,h,z1);put(cTop,k,.9+.2*hash2(i,l));qf(0,1,2,3,R_,G_,B_,1,0);}
    // the face toward you, a block at a time, with a paler lip along the top edge
    vtx(0,x0,h,z0);vtx(1,x1,h,z0);vtx(4,x0,h-.13,z0);vtx(5,x1,h-.13,z0);put(cLip,k,1);qf(0,1,5,4,R_,G_,B_,1,0);
    for(let r=0,yt=h-.13;yt>bot&&r<14;r++){const yb=h-(r+1)*Q;vtx(0,x0,yt,z0);vtx(1,x1,yt,z0);vtx(2,x1,yb,z0);vtx(3,x0,yb,z0);put(((i+r)&1)?cF1:cF2,k,.92+.16*hash2(i*7+r,l+3));qf(0,1,2,3,R_,G_,B_,1,0);yt=yb;}
    // the side that faces the middle of the view, where the next column is lower or missing
    if(x0>camX){const hn=gap(l,i-1)?bot:hgt(l,i-1);if(hn<h){vtx(0,x0,h,z0);vtx(1,x0,h,z1);vtx(2,x0,hn,z1);vtx(3,x0,hn,z0);put(cSideL,k,1);qf(0,1,2,3,R_,G_,B_,1,0);}}
    else if(x1<camX){const hn=gap(l,i+1)?bot:hgt(l,i+1);if(hn<h){vtx(0,x1,h,z0);vtx(1,x1,h,z1);vtx(2,x1,hn,z1);vtx(3,x1,hn,z0);put(cSideR,k,1);qf(0,1,2,3,R_,G_,B_,1,0);}}}
   if(l>3)continue;
   // slabs that hover, each with a gem turning over it
   for(let n=Math.floor(i0/6);n<=Math.ceil(i1/6);n++){const h1=hash2(n,l+50);if(h1<.3)continue;const xa=(n*6+1+2.5*hash2(n,l+60))*CW,xb=xa+2*CW,y1=BASE[l]+Q*AMP[l]*.5+1.2+1.7*hash2(n,l+70)+.3*Math.sin(t*.8+n*2.1+l),y0=y1-.34,za=z0+.3,zb=z0+1.9;
    vtx(0,xa,y1,za);vtx(1,xb,y1,za);vtx(2,xb,y1,zb);vtx(3,xa,y1,zb);vtx(4,xa,y0,za);vtx(5,xb,y0,za);vtx(6,xb,y0,zb);vtx(7,xa,y0,zb);vtx(8,xa,y1-.1,za);vtx(9,xb,y1-.1,za);
    if(y1<camY){put(cTop,k,1);qf(0,1,2,3,R_,G_,B_,1,0);}else{put(cUnder,k,1);qf(4,5,6,7,R_,G_,B_,1,0);}
    put(cLip,k,1);qf(0,1,9,8,R_,G_,B_,1,0);put(cF2,k,1);qf(8,9,5,4,R_,G_,B_,1,0);
    if(xa>camX){put(cSideL,k,1);qf(0,3,7,4,R_,G_,B_,1,0);}else if(xb<camX){put(cSideR,k,1);qf(1,2,6,5,R_,G_,B_,1,0);}
    const gx=(xa+xb)/2,gy=y1+1+.12*Math.sin(t*1.6+n*1.3),gz=(za+zb)/2;if(proj(gx,gy,gz)){glow(SX,SY,Math.min(30,1.5*F/SZ),lc[0],lc[1],lc[2],.42*(1-k*.6));GEM.k=k*.7;drawGem(gx,gy,gz,.3,.48,t*1.3+n*1.7,1);}}}
  present(ctx,.26,185,.5);};
})();

// =====================================================================
// 9. Overworld: the one in chunky pixels. A tile map lies flat and streams toward you as the view
// flies over it and slowly turns: chequered ground, pale winding paths with gems turning above
// them, dark water, and blocks that stand up out of the map as they come near. A skyline of far
// blocks sits on the horizon.
// =====================================================================
const overworld=(()=>{
 const MS=256,map=new Uint8Array(MS*MS);let built=false;
 // tile kinds: 0 ground, 1 path, 2 water, 3 block, 4 path with a gem over it, 6 bridge
 const pn=(x,z,cell,seed)=>{const n=MS/cell,fx=x/cell,fz=z/cell,ix=Math.floor(fx),iz=Math.floor(fz),ux=fx-ix,uz=fz-iz,sx=ux*ux*(3-2*ux),sz=uz*uz*(3-2*uz),h=(a,b)=>hash2(((a%n)+n)%n+seed,((b%n)+n)%n+seed*1.7);
  return lerp(lerp(h(ix,iz),h(ix+1,iz),sx),lerp(h(ix,iz+1),h(ix+1,iz+1),sx),sz);};
 const build=()=>{built=true;for(let tz=0;tz<MS;tz++)for(let tx=0;tx<MS;tx++){const x=tx+.5,z=tz+.5,w=pn(x,z,32,3)*.6+pn(x,z,16,9)*.4,water=w<.38,
   path=fract((x+22*pn(x,z,32,21))/32)<2/32||fract((z+22*pn(x,z,32,33))/32)<2/32;
   map[tz*MS+tx]=path?(water?6:((tx*3+tz*5)%11===0?4:1)):water?2:(pn(x,z,8,40)>.6&&hash2(tx,tz)>.5)?3:0;}};
 // one texel of a tile as a shade 0 (darkest) to 7 (lightest)
 const texel=(k,u,v,t,tx,tz)=>{switch(k){
  case 1:case 4:return (u===0||v===0)?4:5;
  case 2:{const ph=(v+tx*5+tz*3+Math.floor(t*2.4))&7;return (ph===0&&((u+tz*3+tx)&7)<3)?3:1;}
  case 3:return (u===0||v===0||u===7||v===7)?1:2;
  case 6:return (v&3)===0?2:(u===0||u===7)?6:4;
  default:return (((tx>>1)+(tz>>1))&1)?3:2;}};
 const AVG=[2.5,4.9,1.15,2,4.9,0,4];
 return (ctx,t,c)=>{if(!built)build();use(target(320,180));const P=tones(c),W=TW,H=TH,hz=50,f=205;
  const cx=14*Math.sin(t*.045)+5*Math.sin(t*.11),cz=2.6*t,yaw=Math.atan2(.63*Math.cos(t*.045)+.55*Math.cos(t*.11),2.6)+.12*Math.sin(t*.07);camYP(cx,3.7,cz,yaw,0,f,W/2,hz);
  const pal=[P.sh(.8),P.sh(.64),P.sh(.5),P.sh(.38),P.sh(.2),P.ti(.06),P.ti(.34),P.ti(.66)],top=P.sh(.6),hc=P.ti(.16),far=P.sh(.3);
  const fogk=z=>Math.pow(clamp((z-12)/44,0,1),1.1);
  for(let y=0;y<H;y++){let j=y*W*3;
   if(y<hz||!planeRow(y,0)){const gy=clamp(y/hz,0,1)*7;
    for(let x=0;x<W;x++,j+=3){// banded sky, the bands dithered into each other; a far skyline of blocks standing still against the turning view
     const a=(yaw+(x+.5-W/2)/f)*9,sk=hz-2-Math.floor(15*vnoise(Math.floor(a*2.2)*.37+5)*vnoise(a*.21+9)+3*vnoise(Math.floor(a*5)*.9));
     if(y>=sk){const m=.3+.7*(y-sk)/(hz-sk);C[j]=far[0]+(hc[0]-far[0])*m;C[j+1]=far[1]+(hc[1]-far[1])*m;C[j+2]=far[2]+(hc[2]-far[2])*m;}
     else{let g=Math.floor(gy+bayer[(y&3)*4+(x&3)]/16)/7;g*=g;C[j]=top[0]+(hc[0]-top[0])*g;C[j+1]=top[1]+(hc[1]-top[1])*g;C[j+2]=top[2]+(hc[2]-top[2])*g;}}continue;}
   const fk=fogk(RTH),det=clamp((RTH-16)/10,0,1);
   for(let x=0;x<W;x++,j+=3){const X=RX0+x*RXS,Z=RZ0+x*RZS,fx=Math.floor(X),fz=Math.floor(Z),tx=fx&255,tz=fz&255,k=map[tz*MS+tx];let s=AVG[k];
    if(det<1){const tv=texel(k,((X-fx)*8)|0,((Z-fz)*8)|0,t,tx,tz);s=tv+(s-tv)*det;}
    const s0=s|0,s1=s0<7?s0+1:7,sf=s-s0,a=pal[s0],b=pal[s1],r=a[0]+(b[0]-a[0])*sf,g=a[1]+(b[1]-a[1])*sf,bl=a[2]+(b[2]-a[2])*sf;C[j]=r+(hc[0]-r)*fk;C[j+1]=g+(hc[1]-g)*fk;C[j+2]=bl+(hc[2]-bl)*fk;}}
  // what stands on the map: blocks, which rise as they come into range, and gems over the path
  GEM.lx=.4;GEM.ly=.8;GEM.lz=-.45;GEM.d=pal[3];GEM.m=pal[6];GEM.l=P.ti(.85);GEM.fog=hc;GEM.al=1;GEM.mode=0;GEM.spec=300;
  const tx0=Math.floor(cx),tz0=Math.floor(cz),sy=Math.sin(yaw),cyw=Math.cos(yaw),cT=P.ti(.1),cA=pal[4],cB=pal[2],put=(col,k)=>{R_=col[0]+(hc[0]-col[0])*k;G_=col[1]+(hc[1]-col[1])*k;B_=col[2]+(hc[2]-col[2])*k;};
  for(let dz=38;dz>=-1;dz--)for(let dx=-30;dx<=30;dx++){const X=tx0+dx,Z=tz0+dz,k=map[(Z&255)*MS+(X&255)];if(k!==3&&k!==4)continue;
   const rx=X+.5-cx,rz=Z+.5-cz,vz=rx*sy+rz*cyw;if(vz<.8||vz>38||Math.abs(rx*cyw-rz*sy)>vz*.85+1.5)continue;const fk=fogk(vz);
   if(k===4){GEM.k=fk;drawGem(X+.5,1.05+.14*Math.sin(t*1.7+X*.9+Z*.7),Z+.5,.3,.5,t*1.5+X+Z,1);continue;}
   const tq=(Z+.5)/2.6,off=Math.abs(X+.5-14*Math.sin(tq*.045)-5*Math.sin(tq*.11)),hh=hash2(X&255,Z&255),h=(hh>.78?1.9:hh>.3?1:.55)*smooth(34,26,vz)*smooth(1.6,3.2,off);if(h<.02)continue;   // none in the way of the flight
   vtx(0,X,h,Z);vtx(1,X+1,h,Z);vtx(2,X+1,h,Z+1);vtx(3,X,h,Z+1);vtx(4,X,0,Z);vtx(5,X+1,0,Z);vtx(6,X+1,0,Z+1);vtx(7,X,0,Z+1);
   put(cT,fk);qf(0,1,2,3,R_,G_,B_,1,0);put(cA,fk);if(cz<Z)qf(0,1,5,4,R_,G_,B_,1,0);put(cB,fk);if(cx<X)qf(0,3,7,4,R_,G_,B_,1,0);else if(cx>X+1){put(pal[5],fk);qf(1,2,6,5,R_,G_,B_,1,0);}}
  const img=T.img,d=img.data,V=T.veil;for(let i=0,n=T.N;i<n;i++){const j=i*3,p=i*4,k=1-.3*V[i];d[p]=C[j]*k;d[p+1]=C[j+1]*k;d[p+2]=C[j+2]*k;d[p+3]=255;}
  const cv=ctx.canvas;if(cv.width===W&&cv.height===H)ctx.putImageData(img,0,0);else{if(!T.cv){T.cv=mkCanvas(W,H);T.cx=T.cv.getContext('2d');}T.cx.putImageData(img,0,0);ctx.save();ctx.imageSmoothingEnabled=false;ctx.drawImage(T.cv,0,0,cv.width,cv.height);ctx.restore();}};
})();

// =====================================================================
// 10. Ring run: a slow flight through a long curving line of large rings over still water. Each
// ring is lit along its inner edge and mirrored below; it grows as you approach, opens around the
// whole frame, and is gone, with the next ones already bending away ahead.
// =====================================================================
const ringRun=(()=>{
 const SP=6.5,RR=2.35,hi=torusMesh(RR,.16,44,8),lo=torusMesh(RR,.16,26,5),M=new Array(9);
 const px=s=>3.2*Math.sin(s*.045)+1.4*Math.sin(s*.019+1),py=s=>3+.7*Math.sin(s*.031+.5),dxs=s=>.144*Math.cos(s*.045)+.0266*Math.cos(s*.019+1),dys=s=>.0217*Math.cos(s*.031+.5);
 return (ctx,t,c)=>{use(target(480,270));const P=tones(c),W=TW,H=TH,sc=t*3;
  camLook(px(sc)-.4,py(sc)+.12,sc,px(sc+9),py(sc+9)-.1,sc+9,290,262,124);
  const top=P.sh(.66),hc=P.ti(.14),wn=P.sh(.66),lc=P.ti(.6);proj(px(sc+90),py(sc+90),sc+90);const xv=SX,yv=SY;
  let hy=H;for(let y=0;y<H;y++)if(planeRow(y,0)){hy=y;break;}
  for(let y=0;y<H;y++){let j=y*W*3;
   if(y<hy){const g=clamp(y/hy,0,1),g2=g*g*g,br=lerp(top[0],hc[0],g2),bg=lerp(top[1],hc[1],g2),bb=lerp(top[2],hc[2],g2),v=(y-yv)/60,v2=v*v;
    for(let x=0;x<W;x++,j+=3){const u=(x+.5-xv)/120,gl=.5/(1+u*u+v2);C[j]=br+lc[0]*gl;C[j+1]=bg+lc[1]*gl;C[j+2]=bb+lc[2]*gl;}continue;}
   planeRow(y,0);const fk=Math.pow(clamp((RTH-3)/40,0,1),.6),v=(2*hy-y-yv)/60,v2=v*v,rp=.07*clamp(10/RTH,0,1);
   for(let x=0;x<W;x++,j+=3){const X=RX0+x*RXS,Z=RZ0+x*RZS,u=(x+.5-xv)/120,gl=.36/(1+u*u+v2),m=1+rp*Math.sin(Z*1.9+1.4*Math.sin(X*.7+Z*.23));
    const r=wn[0]*m,g=wn[1]*m,b=wn[2]*m;C[j]=r+(hc[0]-r)*fk+lc[0]*gl;C[j+1]=g+(hc[1]-g)*fk+lc[1]*gl;C[j+2]=b+(hc[2]-b)*fk+lc[2]*gl;}}
  MT.lx=.3;MT.ly=.85;MT.lz=-.43;MT.amb=.3;MT.d=P.sh(.84);MT.m=P.sh(.5);MT.l=P.sh(.08);MT.spec=.16;MT.shin=20;MT.rim=.2;MT.rc=P.ti(.5);MT.fog=hc;MT.fn=4;MT.fr=62;MT.k=1;
  const n0=Math.ceil((sc+.9)/SP);let cx=0,cy=0,cz=0,dim=1;
  const shade=(wx,wy,wz,nx,ny,nz)=>{lit(wx,wy,wz,nx,ny,nz);const rx=wx-cx,ry=wy-cy,rz=wz-cz,nr=-(nx*rx+ny*ry+nz*rz)/Math.sqrt(rx*rx+ry*ry+rz*rz),e=nr>0?nr*nr*(1-clamp((SZ-4)/62,0,1))*smooth(1,7,SZ)*1.5:0;R_=(R_+lc[0]*e)*dim;G_=(G_+lc[1]*e)*dim;B_=(B_+lc[2]*e)*dim;};
  const ring=(n,flip)=>{const s=n*SP;let tx=dxs(s),ty=dys(s),tz=1;const l=1/Math.hypot(tx,ty,tz);tx*=l;ty*=l;tz*=l;const rl=1/Math.hypot(tx,tz),rx=tz*rl,rz=-tx*rl;
   M[0]=rx;M[1]=ty*rz;M[2]=tx;M[3]=0;M[4]=(tz*rx-tx*rz)*flip;M[5]=ty*flip;M[6]=rz;M[7]=-ty*rx;M[8]=tz;cx=px(s);cy=py(s)*flip;cz=s;
   drawMesh(s-sc<22&&flip>0?hi:lo,M,1,cx,cy,cz,shade,flip>0?1:.3,flip>0?0:5);};
  dim=.8;for(let k=11;k>=0;k--)ring(n0+k,-1);
  D.fill(0);dim=1;for(let k=11;k>=0;k--)ring(n0+k,1);
  present(ctx,.26,150,.75);};
})();

window.BT_IDEAS_BG=[
 {name:'Save point',line:'A pillar of light stands on a dark floor and sparks spiral up through it.',size:[480,270],pixel:false,draw:savePoint},
 {name:'Loose pixels',line:'Small square chips are carried up a winding current through shafts of light, flashing as they turn.',size:[480,270],pixel:false,draw:loosePixels},
 {name:'Lift-off',line:'Blocks come loose from a floor of blocks and float up into a spotlight, turning as they go.',size:[480,270],pixel:false,draw:liftOff},
 {name:'Pickup trail',line:'A slow flight along a winding line of spinning gems that hover over a glossy floor.',size:[480,270],pixel:false,draw:pickupTrail},
 {name:'Thumbstick',line:'One large analogue stick, softly lit, rolled and flicked by an unseen thumb.',size:[480,270],pixel:false,draw:thumbstick},
 {name:'Button field',line:'A field of round buttons presses down and springs back in travelling waves, each glowing as it goes.',size:[480,270],pixel:false,draw:buttonField},
 {name:'Ball run',line:'A banded ball rolls ahead of you down a curving checker tunnel toward the light.',size:[480,270],pixel:false,draw:ballRun},
 {name:'Side-scroller',line:'Terraces of blocks slide past at five depths, with floating platforms and gems, like a level scrolling by.',size:[480,270],pixel:false,draw:sideScroller},
 {name:'Overworld',line:'A tile map of paths, water and blocks streams toward you in chunky pixels, turning slowly.',size:[320,180],pixel:true,draw:overworld},
 {name:'Ring run',line:'A slow flight through a curving line of large glowing rings over still water.',size:[480,270],pixel:false,draw:ringRun},
];
})();
