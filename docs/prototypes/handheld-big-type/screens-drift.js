/* Big Type Screens: three options for the idle artwork page. Loaded by proposals-screens.html after screens-engine.js.
   What today's page holds, all of it kept in each option: it starts after 15 seconds without a press or a touch, walks the recently
   played games newest first (6 seconds each, once round, then rests on the newest), shows each game's artwork with "Recently played",
   its name, its achievements, its console, its hours and when it was last played, keeps the clock, shows no hints, never changes the
   bottom screen, and ends on any press or touch (A, B, X and Y only wake it; a direction wakes it and moves). */
(()=>{
const B=window.BTS;if(!B)return;const {el,esc,roll,rise,RM,SPRING,EASEIN,WIPE,mix}=B;
const REC=[
 {t:'Okami',con:'playstation2',bd:'ps2-orbs-v3',c:'#2B5BC7',sys:'PlayStation 2',h:'22 h 15 min',a:'9 / 24',when:'today',
  art:{sky:['#262b58','#b9625c','#f0c08a'],sun:{x:.7,y:.4,r:.14,c:'#fbeed6',c2:'#f3c9a0',glow:'rgba(255,190,120,.5)'},ridges:['#7d5c7c','#57426c','#372b52','#1e1836'],base:.5,seed:3,mist:'rgba(255,205,175,.14)',motes:{c:'#ffd3d9',n:26}}},
 {t:'Metroid Prime',con:'gamecube',bd:'gc-caustics',c:'#5548C8',sys:'GameCube',h:'12 h 40 min',a:'14 / 50',when:'yesterday',
  art:{sky:['#0b1220','#1c3c4c','#c96f38'],stars:1,sun:{x:.74,y:.34,r:.2,c:'#f0c08c',c2:'#b5643a',glow:'rgba(240,140,60,.42)'},ring:'rgba(255,225,190,.5)',ridges:['#3d3344','#2b2536','#1b1824','#0f0d15'],base:.56,seed:8,mist:'rgba(255,150,90,.1)',motes:{c:'#ffb070',n:22}}},
 {t:'Super Mario Galaxy',con:'wii',bd:'wii-pointer-v2',c:'#198098',sys:'Wii',h:'19 h 30 min',a:'38 / 60',when:'3 days ago',
  art:{sky:['#060a22','#14205c','#3c2c80'],stars:1,sun:{x:.76,y:.62,r:.3,c:'#9bd6ee',c2:'#2f6f9c',glow:'rgba(120,190,255,.4)'},ring:'rgba(255,255,255,.4)',ridges:['#191546','#0e0c2c'],base:.84,seed:2,motes:{c:'#ffffff',n:30}}}];
const HOMEC='#2B5BC7';
const BROWSE={kicker:'PlayStation 2',word:'PS2',meta:'17 games',meta2:'Last played: Okami',hints:[['A','Open'],['Y','More']]};
const told=g=>({kicker:'Recently played',word:g.t,meta:`${g.h}  ·  ${g.a} achievements`,meta2:`${g.sys}  ·  last played ${g.when}`,hints:[]});
const STEPS=[
 {label:'Browsing, then no press',ms:2600},
 {label:'After 15 seconds: the artwork arrives',g:0,ms:4600},
 {label:'6 seconds later: the next game',g:1,ms:4600},
 {label:'And the next',g:2,ms:4600},
 {label:'Any press wakes it',key:'A',ms:2800}];

/* the bottom screen: home, as the approved mock draws it. The idle page never changes it. */
const GL={clock:'<svg viewBox="0 0 48 48"><circle cx="24" cy="24" r="16"/><path d="M24 15v10l6.5 4"/></svg>',star:'<svg viewBox="0 0 48 48"><path d="M24 8.5l4.6 9.4 10.4 1.5-7.5 7.3 1.8 10.3L24 32.1l-9.3 4.9 1.8-10.3L9 19.4l10.4-1.5z"/></svg>',
 sliders:'<svg viewBox="0 0 48 48"><path d="M9 14h30M9 24h30M9 34h30"/><circle cx="18" cy="14" r="4" fill="currentColor"/><circle cx="31" cy="24" r="4" fill="currentColor"/><circle cx="21" cy="34" r="4" fill="currentColor"/></svg>'};
const HOME=[{g:'clock'},{m:'PS2'},{m:'GC'},{m:'Wii'},{m:'PS1'},{m:'PS3'},{m:'PSP'},{m:'3DS'},{m:'DS'},{m:'GBA'},{g:'star'},{g:'sliders'},{c:'Ico',h:214},{c:'Super Mario Galaxy',h:350},{c:'Gran Turismo 4',h:205},null,null,null,null,null];
function home(D){
 const H=D.layer('home');D.flood(HOMEC,310,270,true);D.bot.style.setProperty('--chip','rgba(255,255,255,.17)');
 const tiles=HOME.map((it,i)=>{const c=i%5,r=i/5|0,t=el('div','tile'+(!it?' empty':it.c?'':' fold')+(i===1?' f':''));t.style.cssText=`left:${54+c*106}px;top:${22+r*106}px;--sz:88px;--tc:${HOMEC}`;t._c=c;t._r=r;
  t.innerHTML='<div class="in">'+(!it?'':it.c?`<div class="cover" style="--a:hsl(${it.h} 40% 47%);--b:hsl(${(it.h+28)%360} 46% 33%)"><i></i><b>${esc(it.c)}</b></div>`:it.g?GL[it.g]:`<span class="mono disp" style="font-size:${it.m.length>2?34:40}px">${it.m}</span>`)+'</div>';H.appendChild(t);return t;});
 const dock=el('div','dock',`<span>${B.GRID}</span>${B.DOCK5.slice(0,4).join('')}<span><svg viewBox="0 0 20 20"><rect x="2" y="3.5" width="16" height="2.6" rx="1.3"/><rect x="2" y="8.7" width="16" height="2.6" rx="1.3"/><rect x="2" y="13.9" width="16" height="2.6" rx="1.3"/></svg></span>`);dock.classList.add('plain');H.appendChild(dock);
 H.appendChild(el('div','qs l','<i>X</i>Swap'));H.appendChild(el('div','qs','1 / 2'));
 D.ringTo({left:160-13,top:22-13,width:114,height:114},{radius:25,jump:true});
 // the idle hop: a wave over the tiles, as the mock's
 return {hop(){if(RM)return;tiles.forEach(t=>t.firstChild.animate([{transform:'translateY(0)'},{transform:'translateY(-10px)'},{transform:'translateY(0)'}],{duration:560,delay:(t._c+t._r)*75,easing:'cubic-bezier(.3,0,.3,1)'}));}};}
// one painted picture that keeps moving
function mkArt(g,w=640,h=360){const cv=document.createElement('canvas');cv.width=w;cv.height=h;const ctx=cv.getContext('2d');let t0=null;
 return {cv,g,draw(t){if(t0==null)t0=t-(B.hash(g.t)%40);try{B.paint(ctx,w,h,t-t0+20,g.art);}catch(e){}}};}

/* 10 · Poster: the artwork takes the whole top screen and the game's name sits on it in big type */
function poster(D){
 const T=B.stage(D),H=home(D);T.backdrop('playstation2','ps2-orbs-v3',HOMEC);let arts=[];
 D.onFrame(t=>arts.slice(-2).forEach(a=>a.draw(t)));
 const veil=el('div','pveil');T.art.after(veil);
 return {states:STEPS,refit(){T.refit();},
  show(i,from){const st=STEPS[i],g=st.g!=null?REC[st.g]:null,was=from>=0&&STEPS[from].g!=null;
   if(st.key)D.press(st.key);if(from>=0)H.hop();
   if(!g){arts.forEach(a=>{const c=a.cv;if(RM)c.remove();else c.animate([{clipPath:'circle(1150px at 480px 560px)'},{clipPath:'circle(0px at 480px 560px)'}],{duration:380,easing:EASEIN,fill:'forwards'}).onfinish=()=>c.remove();});arts=[];
    veil.style.opacity=0;T.hide=false;T.lock=null;T.tone();T.set(BROWSE,was?'back':'open');return;}
   const a=mkArt(g);a.cv.className='artc push';a.draw(performance.now()/1000);T.art.appendChild(a.cv);arts.push(a);const old=arts.slice(0,-1);
   const done=()=>{old.forEach(o=>o.cv.remove());arts=arts.filter(x=>!old.includes(x));T.hide=true;};
   if(RM)done();else a.cv.animate([{clipPath:'circle(0px at 480px 560px)'},{clipPath:'circle(1150px at 480px 560px)'}],{duration:820,delay:60,easing:WIPE,fill:'backwards'}).onfinish=done;
   veil.style.opacity=1;T.lock=false;T.tone();T.set({...told(g),W:760},was?'U':'open');}};}

/* 11 · Through the letters: the console's backdrop stays, and the artwork shows through the game's name */
function through(D){
 const T=B.stage(D),H=home(D);T.backdrop('playstation2','ps2-orbs-v3',HOMEC);
 const dim=el('div','pdim');T.art.before(dim);
 const cv=el('canvas','artc');cv.width=960;cv.height=540;T.art.appendChild(cv);const ctx=cv.getContext('2d'),tmp=document.createElement('canvas'),msk=document.createElement('canvas');tmp.width=msk.width=960;tmp.height=msk.height=540;const tx=tmp.getContext('2d'),mx=msk.getContext('2d');
 let cur=null,old=null,art=null;
 const FACE='900 100px "M PLUS Rounded 1c","Hiragino Maru Gothic ProN","Arial Rounded MT Bold",sans-serif';
 // the name set as large as the screen will take: at most three lines, 872 wide, 316 tall
 function lay(text){const words=text.split(' '),W=872,Hm=316;let lo=40,hi=380,best=null;
  const tryAt=sz=>{ctx.font=FACE.replace('100px',sz+'px');const sp=ctx.measureText(' ').width*.6,lines=[];let line=[],w=0;
   for(const wd of words){const ww=ctx.measureText(wd).width;if(line.length&&w+sp+ww>W){lines.push(line);line=[];w=0;}if(ww>W)return null;w+=(line.length?sp:0)+ww;line.push(wd);}lines.push(line);
   if(lines.length>3||lines.length*sz*.9>Hm)return null;return {sz,lines,sp};};
  for(let k=0;k<10;k++){const m=(lo+hi)/2,r=tryAt(m);if(r){best=r;lo=m;}else hi=m;}
  best=best||tryAt(40)||{sz:40,lines:[words],sp:10};ctx.font=FACE.replace('100px',best.sz+'px');
  const L=[],n=best.lines.length;best.lines.forEach((ln,li)=>{let x=44;const y=414-(n-1-li)*best.sz*.9;ln.forEach(wd=>{for(const ch of wd){const w=ctx.measureText(ch).width;L.push({ch,x,y});x+=w-best.sz*.02;}x+=best.sz*.02+best.sp;});});
  return {sz:best.sz,L};}
 const back=p=>{p=Math.max(0,Math.min(1,p));const c1=1.9,c3=c1+1;return 1+c3*Math.pow(p-1,3)+c1*Math.pow(p-1,2);};
 function text(c,S,t,fill,dy0){c.font=FACE.replace('100px',S.lay.sz+'px');c.textBaseline='alphabetic';c.fillStyle=fill;c.strokeStyle='rgba(255,255,255,.9)';c.lineWidth=3;c.lineJoin='round';const n=S.lay.L.length,per=Math.min(.026,.3/Math.max(n,1));
  S.lay.L.forEach((l,i)=>{let a=1,dy=0;
   if(!RM&&S.out!=null){const p=(t-S.out-i*.008)/.18;if(p>=1)return;if(p>0){a=1-p;dy=-p*.5*S.lay.sz;}}
   else if(!RM){const p=(t-S.t0-.09-i*per)/.54;if(p<=0)return;a=Math.min(1,p*3);dy=(1-back(p))*.95*S.lay.sz;}
   c.globalAlpha=a;if(fill==='edge')c.strokeText(l.ch,l.x,l.y+dy);else c.fillText(l.ch,l.x,l.y+dy+(dy0||0));});c.globalAlpha=1;}
 function frame(t){ctx.clearRect(0,0,960,540);
  for(const S of [old,cur]){if(!S)continue;if(S.out!=null&&t-S.out>.4){if(S===old)old=null;continue;}
   text(ctx,S,t,'rgba(8,8,22,.42)',7);
   S.art.draw(t);tx.globalCompositeOperation='source-over';tx.clearRect(0,0,960,540);tx.drawImage(S.art.cv,-300,-130,1400,788);tx.fillStyle='rgba(255,255,255,.12)';tx.fillRect(0,0,960,540);mx.clearRect(0,0,960,540);text(mx,S,t,'#fff',0);tx.globalCompositeOperation='destination-in';tx.drawImage(msk,0,0);
   ctx.drawImage(tmp,0,0);text(ctx,S,t,'edge',0);}}   // a thin white edge keeps the name readable whatever the artwork is
 let lastT=0;D.onFrame(t=>{lastT=t;if(cur||old)frame(t);else ctx.clearRect(0,0,960,540);});
 const now=()=>lastT;   // the engine's own clock, so a step and the frames after it agree
 return {states:STEPS,refit(){T.refit();if(cur)cur.lay=lay(cur.g.t);},
  show(i,from){const st=STEPS[i],g=st.g!=null?REC[st.g]:null,was=from>=0&&STEPS[from].g!=null;
   if(st.key)D.press(st.key);if(from>=0)H.hop();
   if(cur){old=cur;old.out=now();cur=null;}
   if(!g){dim.style.opacity=0;T.lock=null;T.backdrop('playstation2','ps2-orbs-v3',HOMEC);T.tone();T.set(BROWSE,was?'back':'open');return;}
   dim.style.opacity=.7;dim.style.background=mix(g.c,'#0B0A18',.82);T.lock=false;T.backdrop(g.con,g.bd,g.c);T.tone();
   T.set({...told(g),word:''},'open');cur={g,art:mkArt(g),lay:lay(g.t),t0:now()+(was?.12:.3)};}};}

/* 12 · Porthole: the console's backdrop stays, and the artwork rises through the hinge as a round window beside the name */
function porthole(D){
 const T=B.stage(D),H=home(D);T.backdrop('playstation2','ps2-orbs-v3',HOMEC);
 const port=el('div','port off','<div class="bobber"><div class="glass"></div></div>');T.art.appendChild(port);const glass=port.querySelector('.glass');let arts=[];
 D.onFrame(t=>arts.slice(-2).forEach(a=>a.draw(t)));
 return {states:STEPS,refit(){T.refit();},
  show(i,from){const st=STEPS[i],g=st.g!=null?REC[st.g]:null,was=from>=0&&STEPS[from].g!=null;
   if(st.key)D.press(st.key);if(from>=0)H.hop();
   if(!g){port.classList.add('off');T.lock=null;T.backdrop('playstation2','ps2-orbs-v3',HOMEC);T.tone();T.set(BROWSE,was?'back':'open');const gone=arts;arts=[];setTimeout(()=>gone.forEach(a=>a.cv.remove()),400);return;}
   const a=mkArt(g,560,560);a.cv.className='artc';a.draw(performance.now()/1000);glass.appendChild(a.cv);arts.push(a);const old=arts.slice(0,-1);
   const done=()=>{old.forEach(o=>o.cv.remove());arts=arts.filter(x=>!old.includes(x));};
   if(RM||!was)done();else a.cv.animate([{clipPath:'circle(0% at 50% 100%)'},{clipPath:'circle(120% at 50% 100%)'}],{duration:760,easing:WIPE,fill:'backwards'}).onfinish=done;
   port.classList.remove('off');T.lock=null;T.backdrop(g.con,g.bd,g.c,670);T.tone();T.set({...told(g),W:420},was?'U':'open');}};}

B.add('drift',[
 {name:'Poster',line:'The artwork takes the whole top screen, and the game’s name sits on it in big type.',build:poster},
 {name:'Through the letters',line:'The console’s backdrop stays. The artwork shows only through the game’s name, set as large as the screen.',build:through},
 {name:'Porthole',line:'The console’s backdrop stays. The artwork rises through the hinge as a round window beside the name.',build:porthole}]);
})();
