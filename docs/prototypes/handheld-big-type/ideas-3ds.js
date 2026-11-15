// Big Type proposals: ten candidate backdrops for Nintendo 3DS.
// Loaded by proposals.html, which reads window.BT_IDEAS_3DS (see mountIdeas there).
// Prototype code only: CPU software rendering into a 400 x 225 buffer, the 3DS top screen's pixel density.
// The engine is this project's own, grown from the fragment-lit 3DS renderer in
// ../handheld-mode/backdrops-motion-study/backdrops.js: a depth buffer, per-pixel lighting through lookup
// tables (diffuse ramp, crisp specular, Fresnel rim), fog that fades into the sky behind, and a soft bloom.
// Every palette is built from the one colour the page passes in: tints toward white, the colour, shades toward wine.
(()=>{
'use strict';
const W=400,H=225,N=W*H,TAU=Math.PI*2,NEAR=.3;
const clamp=(v,a,b)=>v<a?a:v>b?b:v,lerp=(a,b,t)=>a+(b-a)*t,mod=(v,n)=>(v%n+n)%n;
const smooth=(a,b,x)=>{const t=clamp((x-a)/(b-a),0,1);return t*t*(3-2*t);};
const hash=n=>{const s=Math.sin(n*127.1+311.7)*43758.5453;return s-Math.floor(s);};
const mix3=(a,b,t)=>[a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t,a[2]+(b[2]-a[2])*t];
const sin=Math.sin,cos=Math.cos,sqrt=Math.sqrt;

// ---------- frame buffers ----------
const C=new Float32Array(N*3),SKY=new Float32Array(N*3),Z=new Float32Array(N); // colour, the sky behind (fog fades to it), 1/depth
const GRAIN=new Float32Array(N);for(let i=0;i<N;i++)GRAIN[i]=hash(i*.731+hash(i*.113)*9.7)-.5; // breaks up 8-bit banding in the soft gradients

// ---------- palette: p(v), v from -1 (deep wine) through 0 (the console's colour) to 1 (white) ----------
let palKey='',palFn=null;
function palette(c){const k=c[0]+','+c[1]+','+c[2];if(k!==palKey){palKey=k;const wine=[c[0]*.2,c[1]*.12,c[2]*.2],white=[255,250,251];palFn=v=>v>=0?mix3(c,white,v):mix3(c,wine,-v);}return palFn;}

// ---------- camera, light, fog (module state, set once per frame) ----------
let EX=0,EY=0,EZ=0,CW=1,SW=0,CP=1,SP=0,F=260,OX=200,OY=112,LX=0,LY=1,LZ=0,VT=null,FN=0,FI=0,FM=0,tx=0,ty=0,tz=0;
const VTC={};
// unit vectors from each pixel back toward the eye, in view space
function viewTable(f,cx,cy){const k=f+'|'+cx+'|'+cy;let t=VTC[k];if(!t){t=VTC[k]=new Float32Array(N*3);for(let y=0,i=0;y<H;y++)for(let x=0;x<W;x++,i+=3){const dx=(x+.5-cx)/f,dy=-(y+.5-cy)/f,l=1/Math.hypot(dx,dy,1);t[i]=-dx*l;t[i+1]=-dy*l;t[i+2]=-l;}}return t;}
function setCam(x,y,z,yaw,pitch,f,cx,cy){EX=x;EY=y;EZ=z;CW=cos(yaw);SW=sin(yaw);CP=cos(pitch);SP=sin(pitch);F=f;OX=cx;OY=cy;VT=viewTable(f,cx,cy);}
function view(x,y,z){x-=EX;y-=EY;z-=EZ;const z1=x*SW+z*CW;tx=x*CW-z*SW;ty=y*CP+z1*SP;tz=-y*SP+z1*CP;}
function vdir(x,y,z){const z1=x*SW+z*CW;tx=x*CW-z*SW;ty=y*CP+z1*SP;tz=-y*SP+z1*CP;}
function wdir(x,y,z){const z1=y*SP+z*CP;ty=y*CP-z*SP;tx=x*CW+z1*SW;tz=-x*SW+z1*CW;} // view direction back to world
function setLight(x,y,z){const l=1/Math.hypot(x,y,z);vdir(x*l,y*l,z*l);LX=tx;LY=ty;LZ=tz;}
function setFog(near,range,max=1){FN=near;FI=1/range;FM=max;}
// The stereo cue: the eye slides sideways while staying aimed at a pivot plane. That plane holds still on the screen,
// nearer things slide one way and farther things the other, like nudging the depth slider.
function sway(t,amp,pivot,y,pitch,f,cx,cy,speed=.33,z=0){const x=sin(t*speed)*amp;setCam(x,y,z,Math.atan2(-x,pivot-z),pitch,f,cx,cy);}

// ---------- lighting lookup tables, the way the 3DS GPU lights a fragment ----------
const DL=new Float32Array(258),FR=new Float32Array(258);
for(let i=0;i<258;i++){const d=i/127.5-1;DL[i]=Math.pow(.35*smooth(-.1,.4,d)+.65*smooth(-.55,1,d),1.15);FR[i]=Math.pow(clamp(1-i/256,0,1),2.6);}
function specLUT(shin,a=.3,b=.6){const s=new Float32Array(2050);for(let i=0;i<2050;i++)s[i]=smooth(a,b,Math.pow(Math.min(1,i/2048),shin));return s;}
const S_GLOSS=specLUT(70),S_TIGHT=specLUT(220),S_SHEEN=specLUT(9,0,1),S_SATIN=specLUT(26,.05,.9);
// material: lo (in shade) to hi (lit), specular table and strength, rim colour and strength, optional tint kc driven by k
function mat(lo,hi,o={}){return {lo,hi,sl:o.sl||S_GLOSS,sp:o.sp??.8,sc:o.sc||[255,247,249],rim:o.rim??.4,rc:o.rc||hi,kc:o.kc||hi,a:o.a??1,con:o.con||0,cw:o.cw||.1,ca:o.ca||.5,cc:o.cc||hi,cf:o.cf||6};}

// One lit fragment. n is the unit normal in view space, iz is 1/depth, cov is edge coverage (anti-aliasing), k tints toward m.kc.
const HN=new Float32Array(2052);for(let i=0;i<2052;i++)HN[i]=1/sqrt(2.0004+2*(i/1024-1));
function shade(i,nx,ny,nz,iz,m,cov,k){
 const j=i*3,vx=VT[j],vy=VT[j+1],vz=VT[j+2];
 let nv=nx*vx+ny*vy+nz*vz;if(nv<0){nx=-nx;ny=-ny;nz=-nz;nv=-nv;}
 const nl=nx*LX+ny*LY+nz*LZ,d=DL[(nl*127.5+127.5)|0],lo=m.lo,hi=m.hi;
 let R=lo[0]+(hi[0]-lo[0])*d,G=lo[1]+(hi[1]-lo[1])*d,B=lo[2]+(hi[2]-lo[2])*d;
 if(k>0){const kc=m.kc,q=k*(.35+.65*d);R+=(kc[0]-R)*q;G+=(kc[1]-G)*q;B+=(kc[2]-B)*q;
  if(m.con>0){const f=k*m.con,fr=f-(f|0);if(fr<m.cw){let a=(1-fr/m.cw)*m.ca*iz*m.cf;if(a>m.ca)a=m.ca;const cc=m.cc;R+=(cc[0]-R)*a;G+=(cc[1]-G)*a;B+=(cc[2]-B)*a;}}}
 if(m.sp>0){const nh=(nl+nv)*HN[((LX*vx+LY*vy+LZ*vz)*1024+1024)|0];if(nh>0){const s=m.sl[(nh*2048)|0]*m.sp;if(s>0){const sc=m.sc;R+=sc[0]*s;G+=sc[1]*s;B+=sc[2]*s;}}}
 if(m.rim>0){const r=FR[(nv*256)|0]*m.rim,rc=m.rc;R+=rc[0]*r;G+=rc[1]*r;B+=rc[2]*r;}
 if(FM>0){let f=(1/iz-FN)*FI;if(f>0){if(f>1)f=1;f*=(2-f)*FM;R+=(SKY[j]-R)*f;G+=(SKY[j+1]-G)*f;B+=(SKY[j+2]-B)*f;}}
 const a=cov*m.a;
 if(a>=1){C[j]=R;C[j+1]=G;C[j+2]=B;Z[i]=iz;}else{C[j]+=(R-C[j])*a;C[j+1]+=(G-C[j+1])*a;C[j+2]+=(B-C[j+2])*a;if(a>.5)Z[i]=iz;}
}

// ---------- triangles: perspective-correct per-pixel normals, depth buffer ----------
const MV=16000,VXs=new Float32Array(MV*3),VNs=new Float32Array(MV*3),SXs=new Float32Array(MV),SYs=new Float32Array(MV),IZs=new Float32Array(MV);
function tri(a,b,c,K,m){
 const iza=IZs[a],izb=IZs[b],izc=IZs[c];if(iza===0||izb===0||izc===0)return;
 const ax=SXs[a],ay=SYs[a],bx=SXs[b],by=SYs[b],cx=SXs[c],cy=SYs[c],area=(bx-ax)*(cy-ay)-(by-ay)*(cx-ax);if(area>-1e-5&&area<1e-5)return;
 const x0=Math.max(0,Math.floor(Math.min(ax,bx,cx))),x1=Math.min(W-1,Math.ceil(Math.max(ax,bx,cx))),y0=Math.max(0,Math.floor(Math.min(ay,by,cy))),y1=Math.min(H-1,Math.ceil(Math.max(ay,by,cy)));if(x0>x1||y0>y1)return;
 const ia=1/area,A0=(by-cy)*ia,B0=(cx-bx)*ia,A1=(cy-ay)*ia,B1=(ax-cx)*ia,px=x0+.5,py=y0+.5;
 const l0r=(bx*cy-by*cx)*ia+A0*px+B0*py,l1r=(cx*ay-cy*ax)*ia+A1*px+B1*py;
 const a3=a*3,b3=b*3,c3=c*3;
 const na=VNs[a3]*iza,nb=VNs[b3]*izb,nc=VNs[c3]*izc,ma=VNs[a3+1]*iza,mb=VNs[b3+1]*izb,mc=VNs[c3+1]*izc,oa=VNs[a3+2]*iza,ob=VNs[b3+2]*izb,oc=VNs[c3+2]*izc;
 const ka=K?K[a]*iza:0,kb=K?K[b]*izb:0,kc=K?K[c]*izc:0;
 const A2=-A0-A1;
 for(let y=y0;y<=y1;y++){const dy=y-y0,r0=l0r+B0*dy,r1=l1r+B1*dy,r2=1-r0-r1;let xs=0,xe=x1-x0;
  // the run of pixels inside all three edges on this row, so nothing outside the triangle is visited
  if(A0>0){const q=Math.ceil(-r0/A0-1e-4);if(q>xs)xs=q;}else if(A0<0){const q=Math.floor(-r0/A0+1e-4);if(q<xe)xe=q;}else if(r0<0)continue;
  if(A1>0){const q=Math.ceil(-r1/A1-1e-4);if(q>xs)xs=q;}else if(A1<0){const q=Math.floor(-r1/A1+1e-4);if(q<xe)xe=q;}else if(r1<0)continue;
  if(A2>0){const q=Math.ceil(-r2/A2-1e-4);if(q>xs)xs=q;}else if(A2<0){const q=Math.floor(-r2/A2+1e-4);if(q<xe)xe=q;}else if(r2<0)continue;
  let l0=r0+A0*xs,l1=r1+A1*xs;
  for(let x=xs,i=y*W+x0+xs;x<=xe;x++,i++,l0+=A0,l1+=A1){const l2=1-l0-l1,iz=l0*iza+l1*izb+l2*izc;if(iz<=Z[i])continue;
   let nx=l0*na+l1*nb+l2*nc,ny=l0*ma+l1*mb+l2*mc,nz=l0*oa+l1*ob+l2*oc;const nl=1/(sqrt(nx*nx+ny*ny+nz*nz)||1);
   shade(i,nx*nl,ny*nl,nz*nl,iz,m,1,K?(l0*ka+l1*kb+l2*kc)/iz:0);}}
}
// world-space vertex buffers (positions, normals, optional tint per vertex) and an index list
function drawBuf(P,Nw,K,idx,nIdx,nVert,m){
 for(let v=0,j=0;v<nVert;v++,j+=3){view(P[j],P[j+1],P[j+2]);VXs[j]=tx;VXs[j+1]=ty;VXs[j+2]=tz;if(tz>NEAR){const iz=1/tz;IZs[v]=iz;SXs[v]=OX+tx*F*iz;SYs[v]=OY-ty*F*iz;}else IZs[v]=0;
  vdir(Nw[j],Nw[j+1],Nw[j+2]);VNs[j]=tx;VNs[j+1]=ty;VNs[j+2]=tz;}
 for(let q=0;q<nIdx;q+=3)tri(idx[q],idx[q+1],idx[q+2],K,m);
}

// ---------- spheres and capsules drawn as impostors: exact round normals per pixel, soft edges, almost no setup cost ----------
function ball(x,y,z,r,m,k=0){
 view(x,y,z);const cz=tz;if(cz<NEAR+r)return;const cxv=tx,cyv=ty,izc=1/cz,sx=OX+cxv*F*izc,sy=OY-cyv*F*izc,R=r*F*izc;
 const x0=Math.max(0,Math.floor(sx-R-1)),x1=Math.min(W-1,Math.ceil(sx+R+1)),y0=Math.max(0,Math.floor(sy-R-1)),y1=Math.min(H-1,Math.ceil(sy+R+1));if(x0>x1||y0>y1)return;
 // a frame that faces the eye, so a sphere near the edge of the picture is lit as correctly as one in the middle
 const il=1/Math.hypot(cxv,cyv,cz),wx=cxv*il,wy=cyv*il,wz=cz*il,ul=1/Math.hypot(wz,wx),ux=wz*ul,uz=-wx*ul,qx=wy*uz,qy=wz*ux-wx*uz,qz=-wy*ux,iR=1/R,hR=R*.5;
 for(let y=y0;y<=y1;y++){const dy=(y+.5-sy)*iR;for(let x=x0;x<=x1;x++){const dx=(x+.5-sx)*iR,e=1-dx*dx-dy*dy,cov=e*hR+.5;if(cov<=0)continue;
  const c=e>0?sqrt(e):0,iz=1/(cz-c*r),i=y*W+x;if(iz<=Z[i])continue;let nx=dx*ux-dy*qx-c*wx,ny=-dy*qy-c*wy,nz=dx*uz-dy*qz-c*wz;
  if(e<=0){const l=1/(sqrt(nx*nx+ny*ny+nz*nz)||1);nx*=l;ny*=l;nz*=l;}
  shade(i,nx,ny,nz,iz,m,cov>1?1:cov,k);}}
}
function cap(x0w,y0w,z0w,x1w,y1w,z1w,r,m,k=0){
 view(x0w,y0w,z0w);let ax=tx,ay=ty,az=tz;view(x1w,y1w,z1w);let bx=tx,by=ty,bz=tz;if(az<NEAR&&bz<NEAR)return;
 if(az<NEAR){const u=(NEAR-az)/(bz-az);ax+=(bx-ax)*u;ay+=(by-ay)*u;az=NEAR;}else if(bz<NEAR){const u=(NEAR-bz)/(az-bz);bx+=(ax-bx)*u;by+=(ay-by)*u;bz=NEAR;}
 const iza=1/az,izb=1/bz,sax=OX+ax*F*iza,say=OY-ay*F*iza,sbx=OX+bx*F*izb,sby=OY-by*F*izb,Ra=r*F*iza,Rb=r*F*izb;
 const x0=Math.max(0,Math.floor(Math.min(sax-Ra,sbx-Rb)-1)),x1=Math.min(W-1,Math.ceil(Math.max(sax+Ra,sbx+Rb)+1)),y0=Math.max(0,Math.floor(Math.min(say-Ra,sby-Rb)-1)),y1=Math.min(H-1,Math.ceil(Math.max(say+Ra,sby+Rb)+1));if(x0>x1||y0>y1)return;
 const ex=sbx-sax,ey=sby-say,el2=ex*ex+ey*ey,iel=el2>1e-6?1/el2:0;
 let dx=bx-ax,dy=by-ay,dz=bz-az;const dl=1/(Math.hypot(dx,dy,dz)||1);dx*=dl;dy*=dl;dz*=dl;
 let mx=-(ax+bx),my=-(ay+by),mz=-(az+bz);const ml=1/Math.hypot(mx,my,mz);mx*=ml;my*=ml;mz*=ml;
 let sx=dy*mz-dz*my,sy=dz*mx-dx*mz,sz=dx*my-dy*mx;const sl=Math.hypot(sx,sy,sz);if(sl<1e-4)sx=1,sy=0,sz=0;else{sx/=sl;sy/=sl;sz/=sl;}
 let fx=sy*dz-sz*dy,fy=sz*dx-sx*dz,fz=sx*dy-sy*dx;if(fx*mx+fy*my+fz*mz<0){fx=-fx;fy=-fy;fz=-fz;}
 const rF=r*F,iel2=el2>1e-6?1/sqrt(el2):0,sg=(ey*sx+ex*sy)>=0?1:-1;
 for(let y=y0;y<=y1;y++){const py=y+.5,qy=py-say;for(let x=x0;x<=x1;x++){const px=x+.5,qx=px-sax,u=(qx*ex+qy*ey)*iel,i=y*W+x;
  if(u<=0||u>=1){const end=u>=1,R=end?Rb:Ra,ddx=(end?px-sbx:qx)/R,ddy=(end?py-sby:qy)/R,e=1-ddx*ddx-ddy*ddy,cov=e*R*.5+.5;if(cov<=0)continue;
   const c=e>0?sqrt(e):0,iz=1/((end?bz:az)+c*r*fz);if(iz<=Z[i])continue;let al=(ddx*ex+ddy*ey)*iel2,s=(ddx*ey-ddy*ex)*iel2*sg;if(e<=0){const l=1/(sqrt(al*al+s*s)||1);al*=l;s*=l;}
   shade(i,al*dx+s*sx+c*fx,al*dy+s*sy+c*fy,al*dz+s*sz+c*fz,iz,m,cov>1?1:cov,k);}
  else{const izu=iza+(izb-iza)*u,R=rF*izu,lx=qx-ex*u,ly=qy-ey*u,s2=(lx*lx+ly*ly)/(R*R),e=1-s2,cov=e*R*.5+.5;if(cov<=0)continue;
   const c=e>0?sqrt(e):0,iz=1/(1/izu+c*r*fz);if(iz<=Z[i])continue;const s=(lx*sx-ly*sy>=0?1:-1)*sqrt(s2>1?1:s2);
   shade(i,s*sx+c*fx,s*sy+c*fy,s*sz+c*fz,iz,m,cov>1?1:cov,k);}}}
}

// ---------- sky: a vertical gradient with soft glows; also what fog fades into ----------
const GL=new Float32Array(1026);for(let i=0;i<1026;i++)GL[i]=Math.exp(-i/64);
function sky(stops,glows){
 for(let y=0;y<H;y++){const u=y/(H-1);let a=stops[0],b=stops[stops.length-1];for(let s=0;s<stops.length-1;s++)if(u>=stops[s][0]&&u<=stops[s+1][0]){a=stops[s];b=stops[s+1];break;}
  const k=b[0]>a[0]?smooth(a[0],b[0],u):0,r=lerp(a[1][0],b[1][0],k),g=lerp(a[1][1],b[1][1],k),bl=lerp(a[1][2],b[1][2],k);
  for(let x=0,j=y*W*3;x<W;x++,j+=3){SKY[j]=r;SKY[j+1]=g;SKY[j+2]=bl;}}
 if(glows)for(const g of glows){const irx=1/(g.rx*g.rx),iry=1/(g.ry*g.ry),col=g.col,kk=g.k;for(let y=0;y<H;y++){const dy=(y+.5-g.y),ey=dy*dy*iry;if(ey>16)continue;for(let x=0,j=y*W*3;x<W;x++,j+=3){const dx=x+.5-g.x,e=dx*dx*irx+ey;if(e>=16)continue;const q=GL[(e*64)|0]*kk;SKY[j]+=(col[0]-SKY[j])*q;SKY[j+1]+=(col[1]-SKY[j+1])*q;SKY[j+2]+=(col[2]-SKY[j+2])*q;}}}
 C.set(SKY);Z.fill(0);
}

// ---------- output: edge anti-aliasing, a gentle contrast curve, 8-bit with a little grain, then a two-step bloom ----------
let oc=null,ox=null,img=null,bA=null,bAx=null,bImg=null,bB=null,bBx=null;
const BW=W>>2,BH=Math.ceil(H/4),LU=new Float32Array(N),CURVE=new Uint8ClampedArray(256);
for(let i=0;i<256;i++){const v=i/255,sm=v*v*(3-2*v);CURVE[i]=Math.round((v+(sm-v)*.4)*255);}
const tone=v=>CURVE[v<0?0:v>255?255:v|0];
function present(ctx,o={}){
 if(!oc){const mk=(w,h)=>{const cv=document.createElement('canvas');cv.width=w;cv.height=h;return cv;};oc=mk(W,H);ox=oc.getContext('2d');img=ox.createImageData(W,H);bA=mk(BW,BH);bAx=bA.getContext('2d');bImg=bAx.createImageData(BW,BH);bB=mk(Math.ceil(BW/3),Math.ceil(BH/3));bBx=bB.getContext('2d');}
 const d=img.data,aa=o.aa!==false;
 if(aa)for(let i=0,j=0;i<N;i++,j+=3){const r=C[j],g=C[j+1],b=C[j+2];LU[i]=(r>255?255:r)*.3+(g>255?255:g)*.59+(b>255?255:b)*.11;}
 // Triangles and ray-cast surfaces have hard pixel edges. This finds each edge, measures how far along its step the pixel sits,
 // and blends across by that much (the FXAA idea), so the picture scales up smooth rather than stair-stepped.
 for(let y=0,i=0,j=0,p=0;y<H;y++)for(let x=0;x<W;x++,i++,j+=3,p+=4){let r=C[j],g=C[j+1],b=C[j+2];
  if(aa&&x>0&&y>0&&x<W-1&&y<H-1){const m=LU[i],n=LU[i-W],s=LU[i+W],e=LU[i+1],w=LU[i-1];let mx=m,mn=m;if(n>mx)mx=n;else if(n<mn)mn=n;if(s>mx)mx=s;else if(s<mn)mn=s;if(e>mx)mx=e;else if(e<mn)mn=e;if(w>mx)mx=w;else if(w<mn)mn=w;
   const range=mx-mn;if(range>7&&range>mx*.09){const nw=LU[i-W-1],ne=LU[i-W+1],sw=LU[i+W-1],se=LU[i+W+1];
    const eh=Math.abs(nw+sw-2*w)+2*Math.abs(n+s-2*m)+Math.abs(ne+se-2*e),ev=Math.abs(nw+ne-2*n)+2*Math.abs(w+e-2*m)+Math.abs(sw+se-2*s),hz=eh>=ev,l1=hz?n:w,l2=hz?s:e,g1=Math.abs(l1-m),g2=Math.abs(l2-m),neg=g1>=g2;
    const grad=.25*(neg?g1:g2),across=hz?(neg?-W:W):(neg?-1:1),along=hz?1:W,avg=.5*((neg?l1:l2)+m),lim1=hz?x-1:y-1,lim2=hz?W-2-x:H-2-y;
    let d1=0,d2=0,e1=0,e2=0,f1=false,f2=false;
    for(let k=1;k<=9;k++){if(!f1){if(k>lim1)f1=true;else{const q=i-along*k;e1=.5*(LU[q]+LU[q+across])-avg;d1=k;if(e1>=grad||e1<=-grad)f1=true;}}
     if(!f2){if(k>lim2)f2=true;else{const q=i+along*k;e2=.5*(LU[q]+LU[q+across])-avg;d2=k;if(e2>=grad||e2<=-grad)f2=true;}}if(f1&&f2)break;}
    const span=d1+d2;let off=0;if(span>0&&(((d1<d2?e1:e2)<0)!==(m<avg)))off=.5-(d1<d2?d1:d2)/span;
    const sub=clamp(Math.abs((2*(n+s+e+w)+nw+ne+sw+se)/12-m)/range,0,1),so=sub*sub*(3-2*sub);const sb=so*so*.38;if(sb>off)off=sb;
    if(off>0){const q=(i+across)*3;r+=(C[q]-r)*off;g+=(C[q+1]-g)*off;b+=(C[q+2]-b)*off;}}}
  const gr=GRAIN[i];d[p]=tone(r+gr);d[p+1]=tone(g+gr);d[p+2]=tone(b+gr);d[p+3]=255;}
 ox.putImageData(img,0,0);
 const amt=o.amt??.4;
 if(amt>0){const thr=o.thr??200,b=bImg.data,inv=1/(255-thr);
  for(let by=0;by<BH;by++)for(let bx=0;bx<BW;bx++){let r=0,g=0,bl=0,n=0;const ye=Math.min(H,by*4+4);for(let yy=by*4;yy<ye;yy++){let j=(yy*W+bx*4)*3;for(let k=0;k<4;k++,j+=3){r+=C[j];g+=C[j+1];bl+=C[j+2];n++;}}
   r/=n;g/=n;bl/=n;const s=clamp(((r*.3+g*.55+bl*.15)-thr)*inv,0,1.5),p=(by*BW+bx)*4;b[p]=r*s;b[p+1]=g*s;b[p+2]=bl*s;b[p+3]=255;}
  bAx.putImageData(bImg,0,0);bBx.imageSmoothingEnabled=true;bBx.drawImage(bA,0,0,bB.width,bB.height);
  ox.save();ox.imageSmoothingEnabled=true;ox.globalCompositeOperation='lighter';ox.globalAlpha=amt;ox.drawImage(bA,0,0,W,H);ox.globalAlpha=amt*(o.wide??.55);ox.drawImage(bB,0,0,W,H);ox.restore();}
 const cv=ctx.canvas;ctx.save();ctx.globalAlpha=1;ctx.globalCompositeOperation='source-over';ctx.imageSmoothingEnabled=true;ctx.drawImage(oc,0,0,cv.width,cv.height);ctx.restore();
}

// ---------- static meshes placed with a rotation and an offset ----------
const WP=new Float32Array(MV*3),WN=new Float32Array(MV*3);
// mesh: {P, Nn, K, I, nv, ni}; M: 3x3 rotation (row-major, may carry a uniform scale) then translation
function drawMesh(g,M,m){const P=g.P,Nn=g.Nn;for(let v=0,j=0;v<g.nv;v++,j+=3){const x=P[j],y=P[j+1],z=P[j+2],a=Nn[j],b=Nn[j+1],c=Nn[j+2];
  WP[j]=M[0]*x+M[1]*y+M[2]*z+M[9];WP[j+1]=M[3]*x+M[4]*y+M[5]*z+M[10];WP[j+2]=M[6]*x+M[7]*y+M[8]*z+M[11];WN[j]=M[0]*a+M[1]*b+M[2]*c;WN[j+1]=M[3]*a+M[4]*b+M[5]*c;WN[j+2]=M[6]*a+M[7]*b+M[8]*c;}
 drawBuf(WP,WN,g.K,g.I,g.ni,g.nv,m);}
const MAT=new Float32Array(12);
function place(rx,ry,rz,s,x,y,z){const a=cos(rx),b=sin(rx),c=cos(ry),d=sin(ry),e=cos(rz),f=sin(rz); // v -> Ry(Rx(Rz v)) * s + offset
 MAT[0]=(e*c+f*b*d)*s;MAT[1]=(-f*c+e*b*d)*s;MAT[2]=a*d*s;MAT[3]=f*a*s;MAT[4]=e*a*s;MAT[5]=-b*s;MAT[6]=(-e*d+f*b*c)*s;MAT[7]=(f*d+e*b*c)*s;MAT[8]=a*c*s;MAT[9]=x;MAT[10]=y;MAT[11]=z;return MAT;}
function meshOf(pos,nor,ks,idx){return {P:new Float32Array(pos),Nn:new Float32Array(nor),K:ks?new Float32Array(ks):null,I:new Uint16Array(idx),nv:pos.length/3,ni:idx.length};}
// a rounded rectangle outline: points and outward normals, counter-clockwise
function rrectPath(a,b,rc,n=8){const o=[];for(const [cx,cy,a0] of [[a-rc,b-rc,0],[-a+rc,b-rc,Math.PI/2],[-a+rc,-b+rc,Math.PI],[a-rc,-b+rc,Math.PI*1.5]])for(let k=0;k<=n;k++){const an=a0+k/n*Math.PI/2;o.push([cx+cos(an)*rc,cy+sin(an)*rc,cos(an),sin(an)]);}return o;}

// ---------- tileable soft noise textures with their slopes, for fog tops and height fields ----------
const NS=256;
function makeNoise(octs){const T=new Float32Array(NS*NS),GX=new Float32Array(NS*NS),GY=new Float32Array(NS*NS);
 for(const [per,sd,amp] of octs){const g=new Float32Array(per*per);for(let i=0;i<per*per;i++)g[i]=hash(i*1.37+sd*17.3);
  for(let y=0;y<NS;y++)for(let x=0;x<NS;x++){const fx=x/NS*per,fy=y/NS*per,x0=Math.floor(fx),y0=Math.floor(fy),ux=fx-x0,uy=fy-y0,sx=ux*ux*ux*(ux*(ux*6-15)+10),sy=uy*uy*uy*(uy*(uy*6-15)+10),x1=(x0+1)%per,y1=(y0+1)%per;
   T[y*NS+x]+=amp*lerp(lerp(g[y0*per+x0],g[y0*per+x1],sx),lerp(g[y1*per+x0],g[y1*per+x1],sx),sy);}}
 let lo=1e9,hi=-1e9;for(let i=0;i<NS*NS;i++){if(T[i]<lo)lo=T[i];if(T[i]>hi)hi=T[i];}for(let i=0;i<NS*NS;i++)T[i]=(T[i]-lo)/(hi-lo);
 for(let y=0;y<NS;y++)for(let x=0;x<NS;x++){GX[y*NS+x]=(T[y*NS+((x+1)&255)]-T[y*NS+((x+255)&255)])*.5;GY[y*NS+x]=(T[((y+1)&255)*NS+x]-T[((y+255)&255)*NS+x])*.5;}
 return {T,GX,GY};}
const N_SOFT=makeNoise([[3,1,.6],[6,2,.3],[12,3,.1]]),N_FINE=makeNoise([[4,5,.5],[8,6,.27],[16,7,.15],[32,8,.08]]);
let ngx=0,ngy=0;
// bilinear value in 0..1; u, v in texture widths. Also leaves the slope (per texel) in ngx, ngy.
function noise(n,u,v){u=(u-Math.floor(u))*NS;v=(v-Math.floor(v))*NS;const xf=Math.floor(u),yf=Math.floor(v),x0=xf&255,y0=yf&255,fx=u-xf,fy=v-yf,x1=(x0+1)&255,y1=(y0+1)&255,a=y0*NS,b=y1*NS,T=n.T;
 const p=T[a+x0],q=T[a+x1],r=T[b+x0],s=T[b+x1];ngx=n.GX[a+x0];ngy=n.GY[a+x0];return p+(q-p)*fx+(r-p+(p-q-r+s)*fx)*fy;}
const EXPT=new Float32Array(4098);for(let i=0;i<4098;i++)EXPT[i]=Math.exp(i/256-8); // exp(x) for x in -8..8
const fexp=x=>x<=-8?0:x>=8?2980.96:EXPT[((x+8)*256)|0];
function fatan2(y,x){const ax=x<0?-x:x,ay=y<0?-y:y,mx=ax>ay?ax:ay,mn=ax>ay?ay:ax,a=mn/(mx+1e-12),s=a*a;let r=((-.0464964749*s+.15931422)*s-.327622764)*s*a+a;if(ay>ax)r=1.57079637-r;if(x<0)r=3.14159274-r;return y<0?-r:r;}

// =====================================================================
// The ideas
// =====================================================================

// ---------- Pearl sheet ----------
function pearlSheet(ctx,t,c){const p=palette(c);
 sky([[0,p(-.98)],[.2,p(-.84)],[.3,p(-.5)],[.42,p(-.84)],[1,p(-.97)]],[{x:215+sin(t*.05)*40,y:62,rx:210,ry:20,col:p(.3),k:.55}]);
 sway(t,.8,8,2.5,.33,260,200,104,.31);setLight(-.25,.5,.82);setFog(4.5,17,1);
 const m=mat(p(-.95),p(-.1),{sl:S_GLOSS,sp:1,rim:.85,rc:p(.45),kc:p(.62)}),dz=.46,flow=t*.42,row=Math.floor(flow/dz),tr=flow-row*dz;
 for(let j=50;j>=0;j--){const z=1.9+j*dz-tr;if(z<1.6)continue;const zw=(j+row)*dz,hw=Math.ceil((z*.86+1)/dz);
  for(let xi=-hw;xi<=hw;xi++){const x=xi*dz,a=sin(x*.36+t*.55)*cos(zw*.24-t*.3),b=sin(zw*.5+x*.22-t*.6),k=clamp(.42+a*.6+b*.3,0,1);
   ball(x,-1.25+.62*a+.26*b,z,.13+.045*k,m,k*k);}}
 present(ctx,{thr:150,amt:.6,aa:false});}

// ---------- Through the screen ----------
const WPATH=rrectPath(3.1,1.86,.8,7),WPN=WPATH.length,WK=new Float32Array(WPN*6),WI=(()=>{const o=[];for(const [r0,r1] of [[0,1],[2,3],[3,4],[4,5]])for(let i=0;i<WPN;i++){const j=(i+1)%WPN,a=r0*WPN,b=r1*WPN;o.push(a+i,a+j,b+j,a+i,b+j,b+i);}return new Uint16Array(o);})();
// one wall with a screen-shaped opening: the inner reveal, a rounded lip, and a face that only reaches as far as the wall in front lets you see
function wall(z,ang,ox,oy,outer,m){const ca=cos(ang),sa=sin(ang),rings=[[-.1,.9,0,.1],[0,0,0,1],[0,0,.5,1],[.12,0,1,.7],[Math.min(1.1,outer*.45),0,1,.12],[outer,0,1,0]];let v=0;
 for(const [off,dz,nf,k] of rings){const a=1-nf,l=1/(Math.hypot(a,nf)||1);for(let i=0;i<WPN;i++,v++){const q=WPATH[i],lx=q[0]+q[2]*off,ly=q[1]+q[3]*off,nx=-q[2]*a*l,ny=-q[3]*a*l,j=v*3;
  WP[j]=ox+lx*ca-ly*sa;WP[j+1]=oy+lx*sa+ly*ca;WP[j+2]=z+dz;WN[j]=nx*ca-ny*sa;WN[j+1]=nx*sa+ny*ca;WN[j+2]=-nf*l;WK[v]=k;}}
 drawBuf(WP,WN,WK,WI,WI.length,v,m);}
function throughScreen(ctx,t,c){const p=palette(c),gx=238+sin(t*.11)*8,gy=106+cos(t*.09)*4;
 sky([[0,p(-.97)],[1,p(-.97)]],[{x:gx,y:gy,rx:120,ry:78,col:p(-.05),k:.95},{x:gx,y:gy,rx:40,ry:26,col:p(.8),k:.9}]);
 sway(t,.34,10,0,0,250,238,106,.3);setLight(.2,.45,.9);setFog(4.2,27,1);
 const n=13,sp=2.3,flow=t*.6,row=Math.floor(flow/sp),tr=flow-row*sp;
 const mA=mat(p(-.985),p(-.55),{sl:S_GLOSS,sp:.5,rim:.9,rc:p(.45),kc:p(.5)}),mB=mat(p(-.975),p(-.45),{sl:S_GLOSS,sp:.5,rim:.9,rc:p(.45),kc:p(.62)});
 let zp=0;for(let k=0;k<n;k++){const z=3.7+k*sp-tr,id=k+row,far=clamp((z-3.7)/20,0,1);
  wall(z,.1*sin(id*.9+t*.13),sin(id*.7+t*.1)*.8*far,cos(id*.55+t*.08)*.45*far,k?3.3*(z/zp-1)+1.3:9,id&1?mA:mB);zp=z;}
 present(ctx,{thr:190,amt:.4});}

// ---------- Fog sea ----------
const FGW=(W>>1)+2,FGH=(H>>1)+2,FGT=new Float32Array(FGW*FGH),FGC=new Float32Array(FGW*FGH*3);
function fogSea(ctx,t,c){const p=palette(c),hz=47,horizon=p(.5);
 sky([[0,p(-.93)],[.11,p(-.62)],[hz/H,horizon],[1,horizon]],[{x:160+sin(t*.04)*30,y:hz+1,rx:210,ry:13,col:p(.92),k:.8}]);
 sway(t,.9,9,2.7,.2,260,200,100,.3);setLight(-.5,.45,.6);setFog(10,40,.75);
 const y0=.5,m=mat(p(-.985),p(-.6),{sl:S_GLOSS,sp:.9,rim:1.1,rc:p(.45)});
 for(let j=12;j>=0;j--)for(let i=-8;i<=8;i++){const x=i*2+(j&1),z=3+j*2,w=.5+.5*sin(t*.33+i*.7+j*.5)*cos(j*.33-t*.16+i*.24),top=y0-.7+3.9*w*w*(.5+.5*hash(i*7.1+j*3.3));
  if(top<y0-.25)continue;cap(x,y0-1,z,x,top-.34,z,.34,m);}
 // The gas: an exponential height fog with a rolling top, integrated along each ray and lit from the slope of its surface.
 // The top and its light are soft, so they are worked out on a half-size grid; the integral is per pixel, so the columns sink into it softly.
 const s=.26,dens=1.6,lit=p(.48),mid=p(-.12),shd=p(-.62),lx=-.64,lz=.77;
 for(let gy=0,q=0;gy<FGH;gy++)for(let gx=0;gx<FGW;gx++,q++){const X0=(gx*2-1-OX)/F,Y0=-(gy*2-1-OY)/F;wdir(X0,Y0,1);const il=1/sqrt(X0*X0+Y0*Y0+1),dy=ty*il,q3=q*3;
  if(dy>-.004){FGT[q]=y0;FGC[q3]=horizon[0];FGC[q3+1]=horizon[1];FGC[q3+2]=horizon[2];continue;}let tp=(y0-EY)/dy;if(tp>90)tp=90;
  const X=EX+tx*il*tp,Zw=EZ+tz*il*tp,n1=noise(N_SOFT,X*.045+t*.007,Zw*.045-t*.011),g1x=ngx,g1z=ngy,n2=noise(N_SOFT,X*.12-t*.012,Zw*.12+t*.018+.4);
  FGT[q]=y0+(n1-.5)*.85+(n2-.5)*.22;const sl=clamp(.5+(g1x*lx+g1z*lz)*24+(ngx*lx+ngy*lz)*8+(n1-.5)*.6,0,1),df=smooth(4,34,tp),a=sl<.5?mix3(shd,mid,sl*2):mix3(mid,lit,sl*2-1);
  FGC[q3]=a[0]+(horizon[0]-a[0])*df;FGC[q3+1]=a[1]+(horizon[1]-a[1])*df;FGC[q3+2]=a[2]+(horizon[2]-a[2])*df;}
 for(let y=0,i=0,j=0;y<H;y++){const g0=((y+1)>>1)*FGW,wy=(y&1)?.25:.75;for(let x=0;x<W;x++,i++,j+=3){const vz=-VT[j+2],dy=-(VT[j+1]*CP-VT[j+2]*SP),iz=Z[i],Ld=iz>0?1/(iz*vz):90,q=g0+((x+1)>>1),wx=(x&1)?.25:.75;
  const t0=FGT[q]+(FGT[q+1]-FGT[q])*wx,t1=FGT[q+FGW]+(FGT[q+FGW+1]-FGT[q+FGW])*wx,top=t0+(t1-t0)*wy;
  let a=(EY-top)/s,b=a+dy*Ld/s;if(b<-5)b=-5;if(b>7.9&&a>7.9)continue;const df=a-b,tau=dens*Ld*(df>.001||df<-.001?(fexp(-b)-fexp(-a))/df:fexp(-a));if(tau<.004)continue;
  const al=1-fexp(-tau),q3=(wy>.5?q+FGW:q)*3+(wx>.5?3:0);C[j]+=(FGC[q3]-C[j])*al;C[j+1]+=(FGC[q3+1]-C[j+1])*al;C[j+2]+=(FGC[q3+2]-C[j+2])*al;}}
 present(ctx,{thr:200,amt:.4});}

// ---------- Ring basin ----------
const RB_SEG=[9,7,11,8,6,9,5,7,5,4],RB_TINT=RB_SEG.map((n,k)=>{const o=new Float32Array(n);for(let i=0;i<n;i++)o[i]=hash(i*3.7+k*17.3)>.7?.55:0;return o;});
function ringBasin(ctx,t,c){const p=palette(c);
 sky([[0,p(-.9)],[1,p(-.9)]]);sway(t,1,13,8.4,.62,400,200,112,.3,-6.5);setLight(-.6,.58,-.35);setFog(0,1,0);
 const cxw=1.9,czw=7.4,K=9,sw=.9,sh=.6,R0=9.5,lhn=sqrt(.6*.6+.35*.35),lhx=-.6/lhn,lhz=-.35/lhn,slen=sh*lhn/.58;
 const mT=mat(p(-.96),p(-.2),{sl:S_GLOSS,sp:.55,rim:.2,rc:p(.4),kc:p(.2)}),mR=mat(p(-.98),p(-.45),{sl:S_GLOSS,sp:.3,rim:.15,rc:p(.4),kc:p(.2)});
 const glow=p(.3),dark=p(-.99),rot=RB_SEG.map((n,k)=>t*(.035+.011*k)*(k&1?1:-1)+k*1.3+40),f0=p(.82),f1=p(.05),breathe=.94+.06*sin(t*.5),Rf=R0-(K-1)*sw;
 for(let i=0,j=0;i<N;i++,j+=3){wdir(-VT[j],-VT[j+1],-VT[j+2]);const dx=tx,dy=ty,dz=tz;if(dy>-.02)continue;
  const dt=-sh/dy,sx=dx*dt,sz=dz*dt;let tk=-EY/dy,px=EX-cxw+dx*tk,pz=EZ-czw+dz*tk,k=0,riser=false,Rk=R0;
  for(;;){Rk=R0-k*sw;if(k===K||px*px+pz*pz>=Rk*Rk)break;const qx=px+sx,qz=pz+sz;if(qx*qx+qz*qz>Rk*Rk){riser=true;break;}px=qx;pz=qz;k++;}
  let nx=0,ny=1,nz=0,sdw=0,ao=1,depth=k/K,tint=0,m=mT;
  if(riser){const a=dx*dx+dz*dz,b=px*dx+pz*dz,cc=px*px+pz*pz-Rk*Rk,u=(-b+sqrt(b*b-a*cc))/a,hx=px+dx*u,hz2=pz+dz*u;let v=-dy*u/sh;if(v>1)v=1;
   nx=-hx/Rk;nz=-hz2/Rk;ny=0;if(v<.14){const w=(1-v/.14)*1.1,l=1/sqrt(1+w*w);nx*=l;ny=w*l;nz*=l;}
   const n=RB_SEG[k],fr0=(fatan2(hz2,hx)+rot[k])*n/TAU,fi=fr0|0,fr=fr0-fi,ds=(fr<.5?fr:1-fr)*TAU*Rk/n;if(ds<.05)ao=.5+ds*10;
   ao*=1-.45*v;depth=(k+v)/K;m=mR;tint=RB_TINT[k][fi%n];}
  else if(k<K){const r=sqrt(px*px+pz*pz),ein=r-Rk,eout=k>0?Rk+sw-r:9;if(ein<.1){const w=(1-ein/.1)*1.15,l=1/sqrt(1+w*w);nx=-px/r*w*l;nz=-pz/r*w*l;ny=l;}
   const n=RB_SEG[k],fr0=(fatan2(pz,px)+rot[k])*n/TAU,fi=fr0|0,fr=fr0-fi,ds=(fr<.5?fr:1-fr)*TAU*r/n;if(ds<.05)ao=.5+ds*10;
   if(eout<.45){const q=eout/.45;ao*=.55+.45*q*(2-q);}tint=RB_TINT[k][fi%n];
   if(k>0){const qx=px+lhx*slen,qz=pz+lhz*slen,e1=sqrt(qx*qx+qz*qz)-(Rk+sw);sdw=e1<-.1?0:e1>.14?1:(e1+.1)/.24;
    if(k>1&&sdw<1){const q2x=px+lhx*slen*2,q2z=pz+lhz*slen*2,e2=sqrt(q2x*q2x+q2z*q2z)-(Rk+2*sw),s2=e2<-.16?0:e2>.24?1:(e2+.16)/.4;if(s2>sdw)sdw=s2;}}}
  else{const r=sqrt(px*px+pz*pz)/Rf;let q=1-r*r;if(q<0)q=0;q*=q*breathe;C[j]=f1[0]+(f0[0]-f1[0])*q;C[j+1]=f1[1]+(f0[1]-f1[1])*q;C[j+2]=f1[2]+(f0[2]-f1[2])*q;continue;}
  vdir(nx,ny,nz);shade(i,tx,ty,tz,1,m,1,tint);
  const dk=1-ao*(1-.75*sdw),g=depth*depth*.62*breathe;let r=C[j],gg=C[j+1],b=C[j+2];r+=(dark[0]-r)*dk;gg+=(dark[1]-gg)*dk;b+=(dark[2]-b)*dk;
  C[j]=r+(glow[0]-r)*g;C[j+1]=gg+(glow[1]-gg)*g;C[j+2]=b+(glow[2]-b)*g;}
 present(ctx,{thr:200,amt:.4});}

// ---------- Strata ----------
const SM=24,SWd=W+2*SM,SHt=H+2*SM,STH=new Float32Array(SWd*SHt),SLW=(SWd>>1)+2,SLH=(SHt>>1)+2,STL=new Float32Array(SLW*SLH);
function strata(ctx,t,c){const p=palette(c),L=8,sc=1/640,o1x=t*.0034,o1y=t*.0021,o2x=-t*.0026,o2y=t*.003;
 for(let y=0,q=0;y<SLH;y++){const yy=y*2-SM,v=yy*sc,by=(.5-yy/H)*.2;for(let x=0;x<SLW;x++,q++){const xx=x*2-SM,u=xx*sc;STL[q]=.66*noise(N_SOFT,u+o1x,v+o1y)+.34*noise(N_SOFT,u*1.9+o2x+.37,v*1.9+o2y+.11)+by+(xx/W-.5)*.26;}}
 for(let y=0,q=0;y<SHt;y++){const a=(y>>1)*SLW,fy=(y&1)*.5;for(let x=0;x<SWd;x++,q++){const b=a+(x>>1),fx=(x&1)*.5,h0=STL[b]+(STL[b+1]-STL[b])*fx,h1=STL[b+SLW]+(STL[b+SLW+1]-STL[b+SLW])*fx;STH[q]=h0+(h1-h0)*fy;}}
 const swx=sin(t*.33)*2,swy=cos(t*.21)*.6,LEV=[],SXk=[],SYk=[],LO=[p(-.99)],HI=[p(-.8)],step=.066,lev0=.27;
 for(let k=0;k<L;k++){LEV.push(lev0+k*step);SXk.push(SM+(k-3)*-swx);SYk.push(SM+Math.round((k-3)*-swy));const v=-.72+k*(1.55/(L-1));LO.push(p(v-.42));HI.push(p(v+.08));}
 // light and half-vector in picture space: x right, y down, z toward the eye
 const Lx=-.5,Ly=-.62,Lz=.6,hl=1/Math.hypot(Lx,Ly,Lz+1),Hx=Lx*hl,Hy=Ly*hl,Hz=(Lz+1)*hl,bw=5.5,so=4.2,G0=.0042,spc=p(1),dkC=p(-1);
 for(let y=0,i=0;y<H;y++)for(let x=0;x<W;x++,i++){let top=-1,hv=0,q=0;const hc=STH[(y+SM)*SWd+x+SM];let k=Math.min(L-1,((hc-lev0)/step+2.2)|0);
  for(;k>=0;k--){const fx=x+SXk[k],xi=fx|0,qq=(y+SYk[k])*SWd+xi,h=STH[qq]+(STH[qq+1]-STH[qq])*(fx-xi);if(h>LEV[k]){top=k;hv=h;q=qq;break;}}
  let nx=0,ny=0,nz=1;
  if(top>=0){const gx=(STH[q+2]-STH[q-2])*.25,gy=(STH[q+2*SWd]-STH[q-2*SWd])*.25,gm=sqrt(gx*gx+gy*gy)+1e-6,e=(hv-LEV[top])/gm;
   if(e<bw){const a=e/bw,cz=sqrt(a*(2-a)),sxy=(1-a)/gm;nx=-gx*sxy;ny=-gy*sxy;nz=cz;}} // a quarter-round edge: the normal swings from sideways at the rim to straight up
  const dif=nx*Lx+ny*Ly+nz*Lz,nh=nx*Hx+ny*Hy+nz*Hz,d=DL[(dif*127.5+127.5)|0],sp=nh>0?S_GLOSS[(nh*2048)|0]*.7:0,lo=LO[top+1],hi=HI[top+1];
  let r=lo[0]+(hi[0]-lo[0])*d,g=lo[1]+(hi[1]-lo[1])*d,bl=lo[2]+(hi[2]-lo[2])*d;
  let sd=0;for(let dd=1;dd<=3;dd++){const kk=top+dd;if(kk>=L)break;const fx=x+SXk[kk]+Lx*so*dd,xi=fx|0,yy=y+SYk[kk]+((Ly*so*dd)|0),qq=yy*SWd+xi,h=STH[qq]+(STH[qq+1]-STH[qq])*(fx-xi);
   if(h>LEV[kk]){const a=clamp((h-LEV[kk])/(G0*(1+dd*2.4)),0,1)*(1.05-dd*.2);if(a>sd)sd=a;}}
  if(sd>0){const a=sd*.62;r+=(dkC[0]-r)*a;g+=(dkC[1]-g)*a;bl+=(dkC[2]-bl)*a;}
  if(sp>0){const a=sp*(1-sd);r+=(spc[0]-r)*a;g+=(spc[1]-g)*a;bl+=(spc[2]-bl)*a;}
  const j=i*3;C[j]=r;C[j+1]=g;C[j+2]=bl;}
 present(ctx,{thr:215,amt:.3});}

// ---------- Long shadows ----------
const SMW=288,SMH=208,SMB=new Float32Array(SMW*SMH),SMX=-9,SMZ=-3,SMS=16; // a shadow map laid on the floor: 16 texels to a unit
function longShadows(ctx,t,c){const p=palette(c);
 sky([[0,p(-.5)],[1,p(-.5)]]);sway(t,1,14,11.5,.9,520,200,112,.3,-6.8);
 const az=t*.1+.9,el=.4,lx=cos(az)*cos(el),ly=sin(el),lz=sin(az)*cos(el),sdx=-cos(az),sdz=-sin(az),cot=cos(el)/sin(el);setLight(lx,ly,lz);setFog(0,1,0);
 SMB.fill(0);const pegs=[],r=.15;
 for(let j=-2;j<=8;j++)for(let i=-6;i<=6;i++){const x=i*1.36+(j&1)*.68+(hash(i*3.1+j*7.7)-.5)*.36,z=j*1.14+(hash(i*5.3+j*2.9)-.5)*.3,w=.5+.5*sin(t*.3+x*.45+z*.6+sin(z*.4-t*.2)),h=.3+.95*w*w;pegs.push([x,z,h]);
  // its shadow: a soft-edged streak on the floor that widens and fades toward the tip
  const len=h*cot,x1=x+sdx*len,z1=z+sdz*len,pad=.5,u0=Math.max(0,Math.floor((Math.min(x,x1)-pad-SMX)*SMS)),u1=Math.min(SMW-1,Math.ceil((Math.max(x,x1)+pad-SMX)*SMS)),v0=Math.max(0,Math.floor((Math.min(z,z1)-pad-SMZ)*SMS)),v1=Math.min(SMH-1,Math.ceil((Math.max(z,z1)+pad-SMZ)*SMS));
  for(let v=v0;v<=v1;v++){const pz=SMZ+(v+.5)/SMS-z;for(let u=u0;u<=u1;u++){const px=SMX+(u+.5)/SMS-x;let s=(px*sdx+pz*sdz)/len;s=s<0?0:s>1?1:s;const ex=px-sdx*len*s,ez=pz-sdz*len*s,d=sqrt(ex*ex+ez*ez),pen=.03+.17*s,a=(r+pen-d)/(2*pen);
   if(a>0){const val=(a>1?1:a)*(1-.45*s*s),q=v*SMW+u;if(val>SMB[q])SMB[q]=val;}}}}
 const flo=p(-.97),fhi=p(-.02),fsh=p(-.9),sc=p(.7),dif=.55+.45*DL[(ly*127.5+127.5)|0];
 for(let y=0,i=0,j=0;y<H;y++)for(let x=0;x<W;x++,i++,j+=3){const vx=VT[j],vy=VT[j+1],vz=VT[j+2];wdir(-vx,-vy,-vz);const wy=ty,tf=-EY/wy,X=EX+tx*tf,Zw=EZ+tz*tf;
  const fu=(X-SMX)*SMS-.5,fv=(Zw-SMZ)*SMS-.5,u0=fu|0,v0=fv|0;let sh=0;if(u0>=0&&v0>=0&&u0<SMW-1&&v0<SMH-1){const q=v0*SMW+u0,a=fu-u0,b=fv-v0,s0=SMB[q]+(SMB[q+1]-SMB[q])*a,s1=SMB[q+SMW]+(SMB[q+SMW+1]-SMB[q+SMW])*a;sh=s0+(s1-s0)*b;}
  const nh=(LY+vy)*HN[((LX*vx+LY*vy+LZ*vz)*1024+1024)|0],sp=nh>0?S_SHEEN[(nh*2048)|0]*.4+S_GLOSS[(nh*2048)|0]*.6:0,far=clamp((tf-13)/9,0,1),lit=dif*(1-.88*sh);
  let rr=flo[0]+(fhi[0]-flo[0])*lit,g=flo[1]+(fhi[1]-flo[1])*lit,b=flo[2]+(fhi[2]-flo[2])*lit;const s=sp*(1-sh)*.5;rr+=(sc[0]-rr)*s;g+=(sc[1]-g)*s;b+=(sc[2]-b)*s;
  C[j]=rr+(fsh[0]-rr)*far;C[j+1]=g+(fsh[1]-g)*far;C[j+2]=b+(fsh[2]-b)*far;}
 const m=mat(p(-.55),p(.6),{sl:S_GLOSS,sp:1,rim:.3,rc:p(.8)});pegs.sort((a,b)=>b[1]-a[1]);for(const [x,z,h] of pegs)cap(x,-.2,z,x,h-r,z,r,m);
 present(ctx,{thr:205,amt:.35});}

// ---------- Card helix ----------
const CARD=(()=>{const path=rrectPath(.95,.57,.17,3),n=path.length,th=.05,pos=[0,0,-th],nor=[0,0,-1],idx=[];
 const ring=(off,z,nz,out)=>{const s=pos.length/3;for(const [x,y,nx,ny] of path){const X=x+nx*off,Y=y+ny*off;pos.push(X,Y,z);const ax=out?nx*out:X*.3,ay=out?ny*out:Y*.3,l=Math.hypot(ax,ay,nz);nor.push(ax/l,ay/l,nz/l);}return s;};
 const r0=ring(-.07,-th,-1,0),r1=ring(0,-th+.025,-.7,.7),r2=ring(0,th-.025,.7,.7),r3=ring(-.07,th,1,0),cb=pos.length/3;pos.push(0,0,th);nor.push(0,0,1);
 for(let i=0;i<n;i++){const j=(i+1)%n;idx.push(0,r0+j,r0+i);for(const [a,b] of [[r0,r1],[r1,r2],[r2,r3]])idx.push(a+i,a+j,b+j,a+i,b+j,b+i);idx.push(cb,r3+i,r3+j);}
 return meshOf(pos,nor,null,idx);})();
function helix(t,ax,az,Rh,count,per,dy,rate,phase,mA,mB,sc){const turn=TAU/per;
 for(let nI=0;nI<count;nI++){const u=nI+t*rate,w=mod(u,count),th=w*turn+phase,y=(w-count/2)*dy;
  drawMesh(CARD,place(-.2,th,0,sc,ax-sin(th)*Rh,y,az-cos(th)*Rh),nI&1?mA:mB);}}
function cardHelix(ctx,t,c){const p=palette(c);
 sky([[0,p(-.97)],[.5,p(-.8)],[1,p(-.98)]],[{x:296+sin(t*.06)*14,y:104,rx:120,ry:130,col:p(-.2),k:.8},{x:296,y:104,rx:40,ry:150,col:p(.2),k:.35}]);
 sway(t,.5,10,0,0,260,200,112,.3);setLight(-.55,.5,-.67);setFog(6.5,9,1);
 const mA=mat(p(-.97),p(-.3),{sl:S_GLOSS,sp:1,rim:.6,rc:p(.3)}),mB=mat(p(-.94),p(-.02),{sl:S_GLOSS,sp:1,rim:.6,rc:p(.45)});
 helix(t,2.5,10,3,48,12,.235,.5,0,mA,mB,1);
 present(ctx,{thr:190,amt:.45});}

// ---------- height-field sheets (satin, valley) ----------
const HGN=64*44,HGP=new Float32Array(HGN*3),HGNn=new Float32Array(HGN*3),HGK=new Float32Array(HGN);let HGI=null,HGIk='';
function sheet(nx,nz,xz,hf,kf,m){ // xz(i,j) leaves the point in tx,tz; hf(x,z) is the height; kf(h,x,z) the tint
 const key=nx+'x'+nz;if(HGIk!==key){HGIk=key;const o=[];for(let j=0;j<nz-1;j++)for(let i=0;i<nx-1;i++){const a=j*nx+i,b=a+1,cc=a+nx,d=cc+1;o.push(a,cc,d,a,d,b);}HGI=new Uint16Array(o);}
 const e=.06;for(let j=0,v=0;j<nz;j++)for(let i=0;i<nx;i++,v++){xz(i,j);const x=tx,z=tz,h=hf(x,z),hx=(hf(x+e,z)-h)/e,hz=(hf(x,z+e)-h)/e,l=1/sqrt(hx*hx+1+hz*hz),q=v*3;HGP[q]=x;HGP[q+1]=h;HGP[q+2]=z;HGNn[q]=-hx*l;HGNn[q+1]=l;HGNn[q+2]=-hz*l;HGK[v]=kf(h,x,z);}
 drawBuf(HGP,HGNn,HGK,HGI,HGI.length,nx*nz,m);}

// ---------- Satin ----------
function satin(ctx,t,c){const p=palette(c);
 sky([[0,p(-.97)],[1,p(-.9)]]);sway(t,.8,10,9.5,.98,330,200,112,.3,-4.5);setLight(-.6,.55,.3);setFog(12,9,.85);
 const ca=cos(.5),sa=sin(.5);
 const hf=(x,z)=>{const u=x*ca+z*sa,v=-x*sa+z*ca,w=.6+.4*sin(v*.5-t*.17+u*.13),ph=u*2.1+1.3*sin(v*.42+t*.2)+.5*sin(v*1.1-u*.3-t*.16)+t*.3,s=sin(ph);return .36*w*s*(.6+.4*s*s)+.1*sin(u*4.4-v*.7+t*.33)*w;};
 const m=mat(p(-.99),p(-.25),{sl:S_SATIN,sp:.55,sc:p(.7),rim:.5,rc:p(-.1),kc:p(.15)});
 sheet(64,44,(i,j)=>{const z=-2+j/43*13.5,hw=6.3+z*.38;tx=(i/63*2-1)*hw;tz=z;},hf,(h)=>clamp(h*1.4+.5,0,1)*.6,m);
 present(ctx,{thr:185,amt:.4});}

// ---------- Contour valley ----------
const VZ=(()=>{const o=[];for(let z=1.25;o.length<42;z*=1.094)o.push(z);return o;})();
function contourValley(ctx,t,c){const p=palette(c),hzn=78;
 sky([[0,p(-.97)],[hzn/H*.55,p(-.72)],[hzn/H,p(.2)],[.5,p(-.2)],[1,p(-.6)]],[{x:230+sin(t*.05)*20,y:hzn-2,rx:210,ry:17,col:p(.72),k:.8},{x:238+sin(t*.05)*20,y:hzn-5,rx:26,ry:13,col:p(1),k:.9}]);
 sway(t,.6,9,1.55,.1,260,200,104,.3);setLight(-.35,.42,.84);setFog(4.5,30,1);
 const Zt=t*1.15,hf=(x,z)=>{const Z=z+Zt,ax=x<0?-x:x,A=smooth(.9,6.5,ax);return -1.15+A*(1.35+.95*sin(x*.31+Z*.19)+.65*sin(Z*.33-x*.24)+.3*sin(x*.7+Z*.52))+.1*sin(Z*.55+x*.9);};
 const m=mat(p(-.99),p(-.4),{sl:S_GLOSS,sp:.7,rim:.7,rc:p(.2),kc:p(.3),con:14,cw:.1,ca:.6,cc:p(.7),cf:11});
 sheet(54,42,(i,j)=>{const z=VZ[j];tx=(i/53*2-1)*(3.6+z*.98)+EX*.5;tz=z;},hf,(h)=>clamp((h+1.3)/4.6,0.001,1),m);
 present(ctx,{thr:195,amt:.45});}

// ---------- Lacquer pool ----------
function lacquerPool(ctx,t,c){const p=palette(c);
 sky([[0,p(-.9)],[1,p(-.9)]]);sway(t,.6,8,2.3,.44,260,200,112,.3);setFog(0,1,0);
 const drops=[],per=1.2,life=7.5;for(let k=Math.floor((t-life)/per);k<=Math.floor(t/per);k++){const age=t-k*per;if(age<0||age>=life)continue;const R=.15+age*1.2,q=1-age/life;drops.push([(hash(k)-.5)*12,1.6+hash(k+.5)*12,R,q*sqrt(q)*.34*Math.min(1,age*2),(R>1.7?(R-1.7)*(R-1.7):0),(R+1.7)*(R+1.7)]);}
 const body=p(-.985),hor=p(.66),zen=p(-.93),pan=p(1),Hc=7,dr=t*.22,nd=drops.length;
 for(let i=0,j=0;i<N;i++,j+=3){wdir(-VT[j],-VT[j+1],-VT[j+2]);const dx=tx,dy=ty,dz=tz,tf=-EY/dy,X=EX+dx*tf,Zw=EZ+dz*tf;
  noise(N_SOFT,X*.035+t*.009,Zw*.035-t*.012);let hx=ngx*5,hz=ngy*5;
  for(let k=0;k<nd;k++){const d=drops[k],ex=X-d[0],ez=Zw-d[1],d2=ex*ex+ez*ez;if(d2>d[4]&&d2<d[5]){const r=sqrt(d2),q=r-d[2],s=-d[3]*sin(q*5.2)*fexp(-q*q*1.3)*5.2/(r+.2);hx+=s*ex;hz+=s*ez;}}
  const il=1/sqrt(hx*hx+1+hz*hz),nx=-hx*il,nz=-hz*il,dn=dx*nx+dy*il+dz*nz,rx=dx-2*dn*nx,rz=dz-2*dn*nz;let ry=dy-2*dn*il;if(ry<.015)ry=.015;
  let cs=1+dn;if(cs<0)cs=0;const c2=cs*cs,fr=.045+.955*c2*c2*cs,e=ry>.5?1:ry*2,eg=e*e*(3-2*e);
  let r=hor[0]+(zen[0]-hor[0])*eg,g=hor[1]+(zen[1]-hor[1])*eg,b=hor[2]+(zen[2]-hor[2])*eg;
  // soft light panels overhead, rounded like the menu's tiles, found by following the reflected ray up to the ceiling
  const th=Hc/ry,u=X+rx*th,v=Zw+rz*th+dr,cu=u-Math.floor(u/9+.5)*9,cv=v-Math.floor(v/6.5+.5)*6.5,qx=(cu<0?-cu:cu)-2.1,qy=(cv<0?-cv:cv)-1.1,sd=(qx>0&&qy>0?sqrt(qx*qx+qy*qy):qx>qy?qx:qy)-.7;
  if(sd<.5){const a=(sd<-.5?1:.5-sd)*(th>60?0:1-th/60);r+=(pan[0]-r)*a;g+=(pan[1]-g)*a;b+=(pan[2]-b)*a;}
  C[j]=body[0]+(r-body[0])*fr;C[j+1]=body[1]+(g-body[1])*fr;C[j+2]=body[2]+(b-body[2])*fr;}
 present(ctx,{thr:150,amt:.5});}

// ---------- Deep wells ----------
function deepWells(ctx,t,c){const p=palette(c),D=10,cw=2.15,ch=1.5,hw=.86,hh=.54,rc=.26,bev=.08;
 sky([[0,p(.5)],[1,p(.5)]]);const sxw=sin(t*.33)*1.4,syw=.3+sin(t*.21+1)*.35;setCam(sxw,syw,-D,Math.atan2(-sxw,D),Math.atan2(syw,D),300,200,112);
 const lwx=-.5,lwy=.6,lwz=-.62,ll=1/Math.hypot(lwx,lwy,lwz),lx=lwx*ll,ly=lwy*ll,lz=lwz*ll;setLight(lx,ly,lz);setFog(0,1,0);
 const mF=mat(p(.1),p(.9),{sl:S_GLOSS,sp:.5,rim:0}),mI=mat(p(-.5),p(.62),{sl:S_GLOSS,sp:.25,rim:0}),deep=p(-.7),dk=p(-.85);
 for(let i=0,j=0;i<N;i++,j+=3){wdir(-VT[j],-VT[j+1],-VT[j+2]);const dx=tx,dy=ty,dz=tz,t0=-EZ/dz,X=EX+dx*t0,Y=EY+dy*t0,ix=Math.floor(X/cw+.5),iy=Math.floor(Y/ch+.5),u=X-ix*cw,v=Y-iy*ch;
  const au=u<0?-u:u,av=v<0?-v:v,qx=au-hw+rc,qy=av-hh+rc,corner=qx>0&&qy>0,sdf=(corner?sqrt(qx*qx+qy*qy):qx>qy?qx:qy)-rc;
  if(sdf>=0){let nx=0,ny=0,nz=-1;if(sdf<bev){const w=(1-sdf/bev)*1.2;let gx=corner?qx:qx>qy?1:0,gy=corner?qy:qx>qy?0:1;const gl=1/sqrt(gx*gx+gy*gy);gx*=gl*(u<0?-1:1);gy*=gl*(v<0?-1:1);const l=1/sqrt(1+w*w);nx=-gx*w*l;ny=-gy*w*l;nz=-l;}
   vdir(nx,ny,nz);shade(i,tx,ty,tz,1,mF,1,0);continue;}
  const w0=.5+.5*sin(t*.42+ix*.8+iy*.55+sin(iy*.9-t*.2)),Dk=.18+1.9*w0*w0,sxz=dx/dz,syz=dy/dz,su=sxz>0?(hw-u)/sxz:sxz<0?(-hw-u)/sxz:1e9,sv=syz>0?(hh-v)/syz:syz<0?(-hh-v)/syz:1e9;
  let s=Dk,nx=0,ny=0,nz=-1,ao=1,sh=0;
  if(su<s||sv<s){if(su<sv){s=su;nx=sxz>0?-1:1;nz=0;}else{s=sv;ny=syz>0?-1:1;nz=0;}ao=1-.5*s/Dk;
   const wdl=nx*lx+ny*ly;if(wdl>0){const reach=-lz/(wdl+.05)*.9;sh=clamp((s-reach*.35)/(.25+reach*.2),0,1);}}
  else{const fu=u+sxz*Dk,fv=v+syz*Dk,dl=Dk/-lz,pu=fu+lx*dl,pv=fv+ly*dl,ax=(pu<0?-pu:pu)-hw+rc,ay=(pv<0?-pv:pv)-hh+rc,sd2=(ax>0&&ay>0?sqrt(ax*ax+ay*ay):ax>ay?ax:ay)-rc,pen=.03+.1*Dk;
   sh=clamp((sd2+pen)/(2*pen),0,1);const e1=hw-(fu<0?-fu:fu),e2=hh-(fv<0?-fv:fv),ed=e1<e2?e1:e2;ao=.72+.28*clamp(ed/.3,0,1);}
  vdir(nx,ny,nz);shade(i,tx,ty,tz,1,mI,1,0);
  const a=1-ao*(1-.6*sh),f=clamp(s/2.2,0,1)*.75;let r=C[j],g=C[j+1],b=C[j+2];r+=(dk[0]-r)*a;g+=(dk[1]-g)*a;b+=(dk[2]-b)*a;
  C[j]=r+(deep[0]-r)*f;C[j+1]=g+(deep[1]-g)*f;C[j+2]=b+(deep[2]-b)*f;}
 present(ctx,{thr:235,amt:.25});}

window.BT_IDEAS_3DS=[
 {name:'Pearl sheet',line:'A rolling sheet of glossy pearls streams toward you out of the dark.',size:[W,H],pixel:false,dark:false,draw:pearlSheet},
 {name:'Through the screen',line:'You glide through an endless run of openings shaped like the top screen.',size:[W,H],pixel:false,dark:false,draw:throughScreen},
 {name:'Fog sea',line:'Rounded columns rise and sink through a rolling sea of fog.',size:[W,H],pixel:false,dark:false,draw:fogSea},
 {name:'Ring basin',line:'A stepped basin of slowly counter-turning rings, glowing from the bottom.',size:[W,H],pixel:false,dark:false,draw:ringBasin},
 {name:'Strata',line:'Stacked contour layers that slowly change shape.',size:[W,H],pixel:false,dark:false,draw:strata},
 {name:'Long shadows',line:'x',size:[W,H],pixel:false,dark:false,draw:longShadows},
 {name:'Card helix',line:'x',size:[W,H],pixel:false,dark:false,draw:cardHelix},
 {name:'Satin',line:'x',size:[W,H],pixel:false,dark:false,draw:satin},
 {name:'Contour valley',line:'x',size:[W,H],pixel:false,dark:false,draw:contourValley},
 {name:'Lacquer pool',line:'x',size:[W,H],pixel:false,dark:false,draw:lacquerPool},
 {name:'Deep wells',line:'x',size:[W,H],pixel:false,dark:true,draw:deepWells},
];
})();
