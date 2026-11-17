/* Big Type Screens: three options for the photo viewer. Loaded by proposals-screens.html after screens-engine.js.
   What today's viewer holds, all of it kept in each option: the picture fitted whole, the game's title, which one this is and what
   kind (1 / 6 · Screenshot), Left and Right to page (wrapping), the video that plays muted on A, Y to fetch missing artwork, B to
   close, a tap that hides the type, and the line that says Loading or what a fetch found. */
(()=>{
const B=window.BTS;if(!B)return;const {el,roll,pulse,RM,SPRING,EASEIN,SOFT}=B;
const COL='#198098',GAME='Super Mario Galaxy';
const PALS=[
 {sky:['#070b24','#16205a','#3a2a7a'],stars:1,sun:{x:.72,y:.6,r:.26,c:'#8fd0ea',c2:'#2f6f9c',glow:'rgba(120,190,255,.4)'},ring:'rgba(255,255,255,.4)',ridges:['#141238'],base:.86,seed:2,motes:{c:'#fff',n:14},hud:1},
 {sky:['#1a0f2e','#5a2a6a','#e08a6a'],stars:1,sun:{x:.3,y:.46,r:.12,c:'#ffe9c4',c2:'#f0a868',glow:'rgba(255,170,110,.5)'},ridges:['#4a2a5c','#2e1a44','#1a1030'],base:.6,seed:5,mist:'rgba(255,190,150,.12)',hud:1},
 {sky:['#04202a','#0b5a66','#8adccb'],sun:{x:.58,y:.3,r:.09,c:'#f4fff0',c2:'#bfe9d2',glow:'rgba(210,255,230,.45)'},ridges:['#1c6a62','#134c4c','#0c3238','#07212a'],base:.5,seed:9,mist:'rgba(200,255,240,.1)',motes:{c:'#eafff6',n:18},hud:1},
 {sky:['#0a0a1e','#2a1a5a','#c0508a'],stars:1,sun:{x:.2,y:.7,r:.34,c:'#f3a0c0',c2:'#8a3c78',glow:'rgba(240,130,190,.35)'},ridges:['#1c1440'],base:.9,seed:13,motes:{c:'#ffd9ec',n:12},hud:1},
 {sky:['#05081c','#101a52','#2c3a9a'],stars:1,sun:{x:.5,y:1.12,r:.5,c:'#7fb4ee',c2:'#22408c',glow:'rgba(120,160,255,.35)'},ridges:[],seed:1,motes:{c:'#fff',n:20},logo:1}];
const ITEMS=[{k:'Screenshot',a:0},{k:'Screenshot',a:1},{k:'Screenshot',a:2},{k:'Screenshot',a:3},{k:'Gameplay video',a:0,vid:1},{k:'Title screen',a:4}],N=ITEMS.length;
// the stand-in pictures are painted once and shared by every thumbnail and every viewer
(()=>{const st=document.createElement('style');let css='';PALS.forEach((P,i)=>{const c=document.createElement('canvas');c.width=640;c.height=360;try{B.paint(c.getContext('2d'),640,360,4+i*9,P);css+=`.rig .pa${i}{background-image:url(${c.toDataURL('image/jpeg',.82)})}`;}catch(e){css+=`.rig .pa${i}{background-color:${P.sky[1]}}`;}});
 st.textContent=css;document.head.appendChild(st);})();
const pic=(it,cls)=>el('div',`ph ${cls} pa${it.a}${it.vid?' vid':''}`,it.vid?'<b class="playm"></b>':'');
const STEPS=[
 {label:'The Photos tab',ms:2400},
 {label:'A opens the photo',open:1,p:0,key:'A',ms:2800},
 {label:'Right: the next one',open:1,p:1,key:'R',ms:2200},
 {label:'Right again',open:1,p:2,key:'R',ms:2200},
 {label:'On to the video',open:1,p:4,key:'R',ms:2600},
 {label:'A plays it muted',open:1,p:4,play:1,key:'A',ms:3200},
 {label:'A tap hides the type',open:1,p:4,play:1,bare:1,ms:2800},
 {label:'B closes',key:'B',ms:2600}];
const kind=st=>st.play?'Playing muted':ITEMS[st.p].k;
// a picture grows out of its thumbnail on the tab, and goes back into it
const THUMB={left:40,top:128,width:164,height:100};
const flip=(e,to,back)=>{if(RM)return null;const f=[{transformOrigin:'0 0',transform:`translate(${THUMB.left-to.left}px,${THUMB.top-to.top}px) scale(${THUMB.width/to.width},${THUMB.height/to.height})`},{transformOrigin:'0 0',transform:'none'}];
 return e.animate(back?f.reverse():f,{duration:back?260:480,easing:back?EASEIN:SOFT,fill:back?'forwards':'none'});};

function option(make){return D=>{
 B.gameOnTop(D,GAME,COL,'Dolphin');
 const C=B.companion(D,{title:GAME,col:COL,tab:2,ach:[38,60],shots:ITEMS.map(()=>''),shotsText:`${N} photos from this game`,dock:B.DOCK5});
 C.el.querySelectorAll('.shot').forEach((e,i)=>{e.classList.add('ph','pa'+ITEMS[i].a);if(ITEMS[i].vid){e.classList.add('vid');e.innerHTML='<b class="playm"></b>';}});
 const L=D.layer('viewer');L.hidden=true;const O=make(D,L);let tok=0;
 return {states:STEPS,refit(){C.fit();},
  show(i,from){const st=STEPS[i],pv=from>=0?STEPS[from]:{},t=++tok;
   if(st.key)D.pressHint(st.key);
   if(!st.open){if(pv.open){L.classList.remove('bare');O.close();setTimeout(()=>{if(t===tok)L.hidden=true;},300);}C.show(!!pv.open);D.ringOff();D.hintsBot([]);return;}
   if(!pv.open){L.hidden=false;L.classList.remove('gone');C.hide();D.ringOff();O.open(st);}
   O.draw(st,pv.open?pv:{});
   D.hintsBot([...(ITEMS[st.p].vid?[['A',st.play?'Stop':'Play muted']]:[]),['Y','Fetch artwork'],['B','Close']]);
   L.classList.toggle('bare',!!st.bare);const hb=D.q('.bhints');if(hb)hb.style.opacity=st.bare?0:1;if(st.bare)D.ring.style.opacity=0;}};};}
// one picture at a time in a box: the next slides in from the side you pressed, the old one leaves quicker
function pager(box,rect,cls){let cur=null;
 return {get cur(){return cur;},
  put(p,dir,first){if(cur&&cur._p===p)return cur;const e=pic(ITEMS[p],cls),old=cur;e.style.cssText=`left:${rect.left}px;top:${rect.top}px;width:${rect.width}px;height:${rect.height}px`;e._p=p;box.appendChild(e);cur=e;
   if(old){if(RM)old.remove();else{old.animate([{},{transform:`translateX(${-dir*40}%)`,opacity:0}],{duration:220,easing:EASEIN,fill:'forwards'}).onfinish=()=>old.remove();e.animate([{transform:`translateX(${dir*100}%)`},{transform:'none'}],{duration:480,easing:SOFT});}}
   else if(first)flip(e,rect);return e;},
  out(){const e=cur;cur=null;if(!e)return;const a=flip(e,rect,true);if(a)a.onfinish=()=>e.remove();else e.remove();}};}

/* 7 · Full screen: the picture and nothing else; the type keeps to the edges */
function full(D,L){
 L.innerHTML=`<div class="pbox" style="left:0;top:0;width:620px;height:540px"></div><div class="pk0 chrome" style="left:40px;top:26px">${GAME}</div><div class="bigh disp fig chrome" style="left:38px;top:426px;font-size:50px"></div><div class="aline chrome kd" style="left:190px;top:454px"></div>`;
 const q=s=>L.querySelector(s),P=pager(q('.pbox'),{left:0,top:68,width:620,height:349},'big');
 return {
  open(){rise(q('.pk0'),.5);},
  close(){P.out();L.classList.add('gone');},
  draw(st,pv){const e=P.put(st.p,st.p>=(pv.p??0)?1:-1,!pv.open);e.classList.toggle('playing',!!st.play);roll(q('.fig'),`${st.p+1} / ${N}`);roll(q('.kd'),kind(st));}};}
const rise=(e,d)=>{if(!RM&&e)e.animate([{transform:'translateY(20px)',opacity:0},{}],{duration:420,delay:d*300,easing:SPRING,fill:'backwards'});};

/* 8 · Strip: the picture above, every picture in a row under it, the ring on the one you are looking at */
function strip(D,L){
 L.innerHTML='<div class="pbox" style="left:0;top:0;width:620px;height:349px;overflow:hidden"></div><div class="thumbs chrome" style="left:0;top:0;width:620px;height:540px"></div><div class="bigh disp fig chrome" style="left:38px;top:432px;font-size:50px"></div><div class="aline chrome kd" style="left:190px;top:460px"></div>';
 const q=s=>L.querySelector(s),P=pager(q('.pbox'),{left:0,top:0,width:620,height:349},'big'),tb=q('.thumbs');
 const th=ITEMS.map((it,i)=>{const e=pic(it,'th');e.style.cssText=`left:${22+i*98}px;top:368px;width:88px;height:50px`;tb.appendChild(e);return e;});
 return {
  open(){if(!RM)th.forEach((e,i)=>e.animate([{translate:'0 60px',opacity:0},{translate:'0 0'}],{duration:440,delay:120+i*40,easing:SPRING,fill:'backwards'}));},
  close(){P.out();D.ringOff();L.classList.add('gone');},
  draw(st,pv){const e=P.put(st.p,st.p>=(pv.p??0)?1:-1,!pv.open);e.classList.toggle('playing',!!st.play);roll(q('.fig'),`${st.p+1} / ${N}`);roll(q('.kd'),`${kind(st)}  ·  ${GAME}`);
   th.forEach((t,i)=>t.classList.toggle('on',i===st.p));D.ringTo({left:22+st.p*98-8,top:360,width:104,height:66},{radius:14,pop:!pv.open});}};}

/* 9 · Reel: the pictures side by side, the next one already showing at the edge, and the count as a big figure */
function reel(D,L){
 L.innerHTML=`<div class="reel" style="left:0;top:0;width:620px;height:330px"></div><div class="pbig disp fig chrome" style="left:36px;top:318px"></div><div class="aline chrome kd" style="left:374px;top:372px;font-size:20px;opacity:1"></div><div class="aline dim chrome" style="left:374px;top:404px">${GAME}</div>`;
 const q=s=>L.querySelector(s),box=q('.reel'),R={left:60,top:40,width:500,height:281};
 const pics=ITEMS.map(it=>{const e=pic(it,'rp');e.style.cssText=`left:${R.left}px;top:${R.top}px;width:${R.width}px;height:${R.height}px`;box.appendChild(e);return e;});
 let back=null,at=0;
 const place=p=>pics.forEach((e,i)=>{const d=i-p;e.style.transform=`translateX(${d*478}px) scale(${d?.86:1})`;e.style.opacity=Math.abs(d)>1?0:d?.45:1;});
 return {
  open(st){if(back){back.cancel();back=null;}place(st.p);box.classList.add('still');flip(pics[st.p],R);setTimeout(()=>box.classList.remove('still'),60);},
  close(){back=flip(pics[at],R,true);L.classList.add('gone');},
  draw(st,pv){at=st.p;place(st.p);pics.forEach((e,i)=>e.classList.toggle('playing',!!st.play&&i===st.p));roll(q('.fig'),`${st.p+1} / ${N}`);roll(q('.kd'),kind(st));}};}

B.add('photos',[
 {name:'Full screen',line:'The picture has the whole screen. The count and the hints keep to the edges.',build:option(full)},
 {name:'Strip',line:'The picture sits above a row of all the pictures, with the ring on the one you are looking at.',build:option(strip)},
 {name:'Reel',line:'The pictures sit side by side with the next one showing at the edge, and the count is a big figure.',build:option(reel)}]);
})();
