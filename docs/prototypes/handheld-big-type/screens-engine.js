/* Big Type Screens: the shared engine for proposals-screens.html.
   Each option on that page is a small live copy of the Thor's two screens at the approved mock's scale (the Stage 960 x 540, the
   Launcher 620 x 540, both half the device). The moves here are copied from the approved mock (index.html): the rolling text, the
   letters that land, the one white ring and its path, the flood. An option is {name, line, build(D)}; build returns
   {states:[{label, ms}], show(i, from)} and show draws state i whole, so the steps can be walked in any order. */
(()=>{
'use strict';
const RM=matchMedia('(prefers-reduced-motion: reduce)').matches;
const SPRING='cubic-bezier(.2,1.5,.35,1)',EASEIN='cubic-bezier(.5,0,.9,.5)',SOFT='cubic-bezier(.2,1.15,.3,1)',WIPE='cubic-bezier(.2,.6,.2,1)';
const esc=s=>String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;');
const rgb=h=>[1,3,5].map(i=>parseInt(h.slice(i,i+2),16));
const mix=(a,b,t)=>{const A=rgb(a),B=rgb(b);return '#'+A.map((v,i)=>Math.round(v+(B[i]-v)*t).toString(16).padStart(2,'0')).join('');};
const hash=s=>{let h=7;for(const c of s)h=(h*31+c.charCodeAt(0))>>>0;return h;};
const el=(tag,cls,html)=>{const e=document.createElement(tag);if(cls)e.className=cls;if(html!=null)e.innerHTML=html;return e;};
const px=r=>({left:r.left+'px',top:r.top+'px',width:r.width+'px',height:r.height+'px'});
const deep=c=>mix(c,'#0B0A18',.64);   // the in-game ground: the console's colour taken most of the way to night, as the mock's launch does

/* rolling text: only the characters that changed roll (the mock's roll) */
function roll(box,text){text=String(text);if(box._t===text)return;box._t=text;
 const sl=[...box.children].filter(s=>!s._dead),n=Math.max(sl.length,text.length);
 for(let i=0;i<n;i++){let s=sl[i];const ch=text[i];
  if(ch===undefined){s._dead=1;s.animate([{opacity:1},{opacity:0}],{duration:140,fill:'forwards'}).onfinish=()=>s.remove();continue;}
  if(!s){s=el('span','sl');box.appendChild(s);}
  if(s._ch===ch)continue;
  const old=s.querySelector('.ch:not(.gone)');
  if(old){old.classList.add('gone');old.animate([{transform:'translateY(0)'},{transform:'translateY(-1.1em)'}],{duration:RM?60:220,delay:RM?0:Math.min(i*12,240),easing:'ease-in',fill:'forwards'}).onfinish=()=>old.remove();}
  const c=el('span','ch');c.textContent=ch===' '?' ':ch;s._ch=ch;s.appendChild(c);
  if(!RM)c.animate([{transform:'translateY(1.1em)'},{transform:'translateY(0)'}],{duration:400,delay:50+Math.min(i*12,240),easing:SPRING,fill:'backwards'});}}

/* a heading whose letters land. o: base (px), max (px wide, shrinks to fit), dir ('U' from below, 'L'/'R' from that side, 'D' from above) */
const FROM={U:[0,.9,6],D:[0,-.7,-5],R:[.55,0,0],L:[-.55,0,0]};
function letters(box,text,o={}){text=String(text);if(box._t===text)return;const had=box._t;box._t=text;
 if(had&&!RM&&box.parentNode){const g=box.cloneNode(true);g.removeAttribute('id');g.classList.add('ghost');box.parentNode.insertBefore(g,box);
  const [ex,ey]=o.dir==='L'?[.45,0]:o.dir==='R'?[-.45,0]:o.dir==='D'?[0,.5]:[0,-.5];
  [...g.children].forEach((s,i)=>s.animate([{opacity:1},{transform:`translate(${ex}em,${ey}em)`,opacity:0}],{duration:170,delay:Math.min(i*8,90),easing:EASEIN,fill:'forwards'}));setTimeout(()=>g.remove(),300);}
 box.innerHTML='';[...text].forEach(ch=>{const s=el('span');s.textContent=ch;box.appendChild(s);});
 if(o.base){box.style.fontSize=o.base+'px';const w=box.offsetWidth;if(o.max&&w>o.max)box.style.fontSize=o.base*o.max/w+'px';}
 if(RM||o.still)return;const [nx,ny,rot]=FROM[o.dir||'U'],ls=[...box.children],n=ls.length,per=Math.min(24,300/Math.max(n,1));
 ls.forEach((s,i)=>s.animate([{transform:`translate(${nx}em,${ny}em) rotate(${rot}deg)`,opacity:0},{transform:'none',opacity:1}],{duration:520,delay:60+(o.dir==='L'?n-1-i:i)*per,easing:SPRING,fill:'backwards'}));}

/* things that rise in, and drop out */
function rise(els,o={}){if(RM)return;[...els].forEach((e,i)=>e.animate([{transform:`translate(${o.dx||0}px,${o.dy??40}px) scale(${o.s||1})`,opacity:0},{}],{duration:o.ms||460,delay:(o.delay||0)+i*(o.stagger??40),easing:SPRING,fill:'backwards'}));}
function pulse(e,from=.9){if(!RM&&e)e.animate([{scale:String(from)},{scale:'1'}],{duration:320,easing:SPRING});}

/* ---------- backdrops: the approved renderers, published beside the page or read from the motion study ---------- */
const bdWait=[];let bdState=0;
function whenBackdrops(cb){if(window.Backdrops){cb(window.Backdrops);return;}bdWait.push(cb);if(bdState)return;bdState=1;
 (function load(srcs){if(!srcs.length)return;const s=document.createElement('script');s.src=srcs[0];
  s.onload=()=>{if(!window.Backdrops){load(srcs.slice(1));return;}bdWait.splice(0).forEach(f=>{try{f(window.Backdrops);}catch(e){}});};
  s.onerror=()=>load(srcs.slice(1));document.head.appendChild(s);})(['backdrops.js','../handheld-mode/backdrops-motion-study/backdrops.js']);}
const tc=document.createElement('canvas');tc.width=32;tc.height=18;const tx=tc.getContext('2d',{willReadFrequently:true});
function lumaOf(cv,x0=0,x1=32){try{tx.drawImage(cv,0,0,32,18);const d=tx.getImageData(x0,0,x1-x0,18).data;let y=0;for(let i=0;i<d.length;i+=4)y+=d[i]*.299+d[i+1]*.587+d[i+2]*.114;return y/(d.length/4);}catch(e){return 90;}}

/* stand-in pictures: a game's own artwork and screenshots go here in the app. Painted, never loaded: a sky, a sun or a planet,
   ridges that slide past each other, and things adrift. P: {sky:[...], sun:{x,y,r,c,glow}, ring, ridges:[...], seed, mist, motes:{c,n}, stars, hud, logo} */
const hf=i=>{const x=Math.sin(i*127.1+1.7)*43758.5453;return x-Math.floor(x);};
function paint(ctx,w,h,t,P){
 const g=ctx.createLinearGradient(0,0,0,h);P.sky.forEach((c,i)=>g.addColorStop(i/(P.sky.length-1),c));ctx.globalAlpha=1;ctx.fillStyle=g;ctx.fillRect(0,0,w,h);
 if(P.stars)for(let i=0;i<90;i++){const x=(hf(i)*w+t*1.5*(.4+hf(i+40)))%w,y=hf(i+99)*h*.78;ctx.globalAlpha=.25+.6*Math.abs(Math.sin(t*.7+i*1.3));ctx.fillStyle='#fff';const r=.6+hf(i+7)*1.2;ctx.fillRect(x,y,r*w/480,r*w/480);}
 ctx.globalAlpha=1;
 if(P.sun){const s=P.sun,sx=s.x*w+Math.sin(t*.07)*w*.01,sy=s.y*h+Math.cos(t*.05)*h*.008,R=s.r*h;
  const rg=ctx.createRadialGradient(sx,sy,R*.4,sx,sy,R*3.4);rg.addColorStop(0,s.glow);rg.addColorStop(1,'rgba(0,0,0,0)');ctx.fillStyle=rg;ctx.fillRect(0,0,w,h);
  const bg=ctx.createRadialGradient(sx-R*.35,sy-R*.35,R*.1,sx,sy,R);bg.addColorStop(0,s.c);bg.addColorStop(1,s.c2||s.c);ctx.fillStyle=bg;ctx.beginPath();ctx.arc(sx,sy,R,0,7);ctx.fill();
  if(P.ring){ctx.strokeStyle=P.ring;ctx.lineWidth=h*.014;ctx.beginPath();ctx.ellipse(sx,sy,R*1.9,R*.42,-.35,0,7);ctx.stroke();}}
 const n=P.ridges.length;P.ridges.forEach((c,i)=>{const y0=h*((P.base||.52)+(1-(P.base||.52))*i/n*.92),amp=h*(.15-.022*i),sd=i*3.7+(P.seed||0);
  if(P.mist){const mg=ctx.createLinearGradient(0,y0-amp,0,y0+amp*1.6);mg.addColorStop(0,'rgba(255,255,255,0)');mg.addColorStop(1,P.mist);ctx.fillStyle=mg;ctx.fillRect(0,y0-amp,w,amp*2.6);}
  ctx.fillStyle=c;ctx.beginPath();ctx.moveTo(0,h);for(let x=0;x<=w+8;x+=8){const u=x/w*5+sd+t*.016*(i+1);ctx.lineTo(x,y0+amp*(Math.sin(u)*.5+Math.sin(u*2.3+sd)*.3+Math.sin(u*5.1+sd*2)*.2));}ctx.lineTo(w,h);ctx.fill();});
 if(P.motes)for(let i=0;i<P.motes.n;i++){const x=(hf(i*3)*w+t*w*(.012+hf(i)*.02))%w,y=((hf(i*7+1)*h+Math.sin(t*.6+i)*h*.03+t*h*.012*(hf(i+5)-.35))%h+h)%h;ctx.globalAlpha=.3+.5*hf(i+11);ctx.fillStyle=P.motes.c;ctx.beginPath();ctx.arc(x,y,(1+2.4*hf(i+2))*w/640,0,7);ctx.fill();}
 ctx.globalAlpha=1;
 if(P.hud){ctx.fillStyle='rgba(255,255,255,.85)';for(let i=0;i<3;i++){ctx.beginPath();ctx.arc(w*(.05+i*.035),h*.09,h*.022,0,7);ctx.fill();}ctx.fillRect(w*.78,h*.07,w*.17,h*.03);ctx.fillStyle='rgba(255,255,255,.4)';ctx.fillRect(w*.78,h*.12,w*.1,h*.02);}
 if(P.logo){ctx.fillStyle='rgba(255,255,255,.92)';const rr=(x,y,ww,hh)=>{ctx.beginPath();ctx.roundRect?ctx.roundRect(x,y,ww,hh,hh/2):ctx.rect(x,y,ww,hh);ctx.fill();};rr(w*.25,h*.3,w*.5,h*.11);rr(w*.33,h*.46,w*.34,h*.07);ctx.fillStyle='rgba(255,255,255,.6)';rr(w*.41,h*.76,w*.18,h*.035);}}

/* ---------- one device ---------- */
const DEVS=[];
const PADL='<span></span><span class="pk" data-k="U">▲</span><span></span><span class="pk" data-k="L">◀</span><span class="mid"></span><span class="pk" data-k="R">▶</span><span></span><span class="pk" data-k="D">▼</span><span></span>';
const PADR='<span></span><span class="pk" data-k="Y">Y</span><span></span><span class="pk" data-k="X">X</span><span></span><span class="pk" data-k="B">B</span><span></span><span class="pk" data-k="A">A</span><span></span>';
function mkDev(){
 const dev=el('div','dev'),rig=el('div','rig');dev.appendChild(rig);
 rig.innerHTML='<div class="scr top"></div><div class="hinge"></div><div class="scr bot"><div class="ground"></div><div class="ring"></div></div>'+
  '<span class="shd l" data-k="LB">LB</span><span class="shd r" data-k="RB">RB</span><div class="pad l" aria-hidden="true">'+PADL+'</div><div class="pad r" aria-hidden="true">'+PADR+'</div>';
 const D={dev,rig,top:rig.querySelector('.top'),bot:rig.querySelector('.bot'),ground:rig.querySelector('.ground'),ring:rig.querySelector('.ring'),vis:false,frames:[],_ring:null,_g:null};
 D.q=s=>rig.querySelector(s);D.qa=s=>[...rig.querySelectorAll(s)];
 D.layer=cls=>{const l=el('div','lay'+(cls?' '+cls:''));D.bot.insertBefore(l,D.ring);return l;};
 // a press shows on the controls beside the bottom screen, so each step says which button did it
 D.press=k=>{const b=rig.querySelector(`[data-k="${k}"]`);if(!b)return;b.classList.add('dn');clearTimeout(b._t);b._t=setTimeout(()=>b.classList.remove('dn'),420);};
 // the one ring: covers the old and new place by 38% of its run, lands by 74%, squashed across its travel (the mock's moveRing)
 D.ringTo=(b,o={})=>{const ring=D.ring,a=D._ring;D._ring=b;ring.style.borderRadius=(o.radius??16)+'px';ring.style.opacity=1;Object.assign(ring.style,px(b));
  if(RM)return;
  if(o.pop||!a){ring.animate([{transform:'scale(1.25)',opacity:0},{transform:'scale(1)',opacity:1}],{duration:320,easing:SPRING});return;}
  if(o.jump)return;
  const u={left:Math.min(a.left,b.left),top:Math.min(a.top,b.top)};u.width=Math.max(a.left+a.width,b.left+b.width)-u.left;u.height=Math.max(a.top+a.height,b.top+b.height)-u.top;
  const same=a.width===b.width&&a.height===b.height,horiz=a.top===b.top;
  const sq=!same?b:horiz?{left:b.left+5,top:b.top-3,width:b.width-10,height:b.height+6}:{left:b.left-3,top:b.top+5,width:b.width+6,height:b.height-10};
  ring.animate([{...px(a),easing:'cubic-bezier(.3,0,.4,1)'},{...px(u),offset:.38,easing:'cubic-bezier(.3,0,.4,1)'},{...px(sq),offset:.74},px(b)],{duration:300});};
 D.ringOff=()=>{D.ring.style.opacity=0;D._ring=null;};
 D.bump=d=>{if(RM)return;const v={L:[-9,0],R:[9,0],U:[0,-9],D:[0,9]}[d]||[0,0];D.ring.animate([{translate:'0 0'},{translate:`${v[0]}px ${v[1]}px`},{translate:'0 0'}],{duration:220,easing:'ease-out'});};
 // the flood: the Launcher's new ground grows as a circle from where the focus is
 D.flood=(g,x=310,y=270,quiet)=>{if(g===D._g)return;D._g=g;D.bot.style.setProperty('--g',g);
  if(quiet||RM){D.ground.querySelectorAll('.wave').forEach(w=>w.remove());D.ground.style.background=g;return;}
  const w=el('div','wave');w.style.background=g;D.ground.appendChild(w);
  w.animate([{clipPath:`circle(0px at ${x}px ${y}px)`},{clipPath:`circle(820px at ${x}px ${y}px)`}],{duration:640,easing:WIPE}).onfinish=()=>{D.ground.style.background=g;w.remove();};};
 D.onFrame=fn=>D.frames.push(fn);
 // hints on the bottom screen, for the times the top screen belongs to the game
 D.hintsBot=(list,bottom=24)=>{let hb=D.q('.bhints');if(!hb){hb=el('div','bhints');D.bot.appendChild(hb);}hb.style.bottom=bottom+'px';const key=list.map(h=>h.join()).join('|');if(hb._k===key)return;hb._k=key;
  hb.innerHTML=list.map(([k,l])=>`<span class="bh" data-h="${k}"><i${k.length>1?' class="w"':''}>${k}</i>${esc(l)}</span>`).join('');
  if(!RM)[...hb.children].forEach((h,i)=>h.animate([{transform:'translateY(12px)',opacity:0},{transform:'none',opacity:1}],{duration:380,delay:i*50,easing:SPRING,fill:'backwards'}));};
 D.pressHint=k=>{const h=D.q(`.bhints [data-h="${k}"] i, .hints [data-h="${k}"] i`);if(h&&!RM)h.animate([{transform:'scale(.72)'},{transform:'scale(1)'}],{duration:320,easing:SPRING});D.press(k);};
 DEVS.push(D);return D;}

/* the Stage as the mock draws it while you browse: backdrop, console name, clock, the big name, two lines, hints */
function stage(D){
 D.top.innerHTML='<div class="bd"></div><div class="shade"></div><div class="art"></div><div class="kick"></div><div class="sys"><span class="batt">76%</span><span class="clock disp"></span></div>'+
  '<div class="word disp"></div><div class="meta"></div><div class="meta2"></div><div class="hints"></div>';
 const T={D,bd:D.q('.top .bd'),art:D.q('.top .art'),kick:D.q('.top .kick'),word:D.q('.top .word'),meta:D.q('.top .meta'),meta2:D.q('.top .meta2'),hints:D.q('.top .hints'),wl:null,L:null,layers:[],col:'#2B5BC7',lock:null};
 roll(D.q('.top .clock'),'18:00');
 const fitWord=(layer,W,H)=>{let lo=28,hi=420;layer.style.width=W+'px';for(let i=0;i<9;i++){const m=(lo+hi)/2;layer.style.fontSize=m+'px';if(layer.scrollWidth<=W+1&&layer.offsetHeight<=H)lo=m;else hi=m;}layer.style.fontSize=Math.floor(lo)+'px';};
 const ENTER={R:[.55,0,0],L:[-.55,0,0],U:[0,-.5,0],D:[0,.5,0],open:[0,.95,7],back:[0,-.7,-5]},EXIT={R:[-.45,0],L:[.45,0],U:[0,.45],D:[0,-.45],open:[0,-.6],back:[0,.6]};
 T.name=(text,dir='open',W=680,H=340)=>{
  if(T.wl&&T.wl._t===text&&T.wl._W===W)return;
  if(T.wl){const old=T.wl,ls=[...old.querySelectorAll('.l')],[ex,ey]=EXIT[dir]||EXIT.open,per=Math.min(12,110/Math.max(ls.length,1));T.wl=null;
   ls.forEach((l,i)=>l.animate([{opacity:1},{transform:`translate(${ex}em,${ey}em)`,opacity:0}],{duration:RM?60:170,delay:RM?0:i*per,easing:EASEIN,fill:'forwards'}));setTimeout(()=>old.remove(),RM?70:320);}
  if(!text)return;
  const w=el('div','wl');w._t=text;w._W=W;w._H=H;
  text.split(' ').forEach(x=>{const ws=el('span','w');[...x].forEach(ch=>{const s=el('span','l');s.textContent=ch;ws.appendChild(s);});w.appendChild(ws);w.appendChild(document.createTextNode(' '));});
  T.word.appendChild(w);fitWord(w,W,H);T.wl=w;if(RM)return;
  const ls=[...w.querySelectorAll('.l')],[nx,ny,rot]=ENTER[dir]||ENTER.open,per=Math.min(26,300/Math.max(ls.length,1)),n=ls.length;
  ls.forEach((l,i)=>l.animate([{transform:`translate(${nx}em,${ny}em) rotate(${rot}deg)`,opacity:0},{transform:'none',opacity:1}],{duration:540,delay:90+(dir==='L'?n-1-i:i)*per,easing:SPRING,fill:'backwards'}));};
 T.refit=()=>{if(T.wl)fitWord(T.wl,T.wl._W,T.wl._H);};
 T.hint=list=>{const key=list.map(h=>h.join()).join('|'),hb=T.hints;if(hb._k===key)return;hb._k=key;
  hb.innerHTML=list.map(([k,l])=>`<span class="hint" data-h="${k}"><i${k.length>1?' class="w"':''}>${k}</i>${esc(l)}</span>`).join('');
  if(!RM)[...hb.children].forEach((h,i)=>h.animate([{transform:'translateY(14px)',opacity:0},{transform:'none',opacity:1}],{duration:380,delay:i*50,easing:SPRING,fill:'backwards'}));};
 T.set=(d,dir)=>{roll(T.kick,d.kicker||'');roll(T.meta,d.meta||'');roll(T.meta2,d.meta2||'');T.hint(d.hints||[]);T.name(d.word,dir,d.W||680);};
 // ink and veil to suit what is behind: white with a night veil, or the console's colour taken toward night with a white veil
 T.ink=light=>{D.top.style.setProperty('--ink',light?mix(T.col,'#0B0A18',.74):'#fff');D.top.style.setProperty('--veil',light?'rgba(255,255,255,.3)':'rgba(11,10,24,.3)');};
 T.tone=()=>{if(T.lock!=null){T.ink(T.lock);return;}if(T.L)T.ink(lumaOf(T.L.canvas,0,20)>140);};
 // the backdrop of a console; a change grows through a circle from below, as the mock's wipe does
 T.backdrop=(con,id,col,x=480)=>{if(col)T.col=col;const key=con+'|'+id;if(T._bk===key)return;T._bk=key;D.top.style.setProperty('--gcol',T.col);
  whenBackdrops(B=>{if(T._bk!==key)return;const v=B.VARIANTS.find(q=>q.id===id)||B.VARIANTS.find(q=>q.con===con);if(!v)return;
   const cv=el('canvas','bdc'),L=B.makeLayer(cv);try{B.prepare(L,v,3+(hash(key)%17));B.render(L,1/30);}catch(e){return;}
   if(cv.classList.contains('pixel'))cv.style.imageRendering='pixelated';
   T.bd.appendChild(cv);T.layers.push(L);T.L=L;T.tone();
   const done=()=>{const i=T.layers.indexOf(L);T.layers.splice(0,i).forEach(o=>o.canvas.remove());};
   if(T.layers.length===1||RM){done();return;}
   cv.animate([{clipPath:`circle(0px at ${x}px 560px)`},{clipPath:`circle(1150px at ${x}px 560px)`}],{duration:760,delay:110,easing:WIPE,fill:'backwards'}).onfinish=done;});};
 let toneAt=0;
 D.onFrame((t,dt)=>{if(T.hide||!T.L)return;T.layers.slice(-2).forEach(L=>{L.time+=dt;try{window.Backdrops.render(L,1/30);}catch(e){}});if(t-toneAt>.9){toneAt=t;T.tone();}});
 return T;}

/* the Stage while a game runs: the game has it. Drawn as the mock's launch leaves it: the console's colour, the title, what runs it. */
function gameOnTop(D,title,col,emu){
 const L=el('div','launch',`<div class="lnote">The game is on this screen</div><div class="lw disp"></div><div class="ls">Running in ${esc(emu)}</div>`);L.style.background=col;D.top.appendChild(L);
 const lw=L.querySelector('.lw');lw.style.fontSize=title.length>22?'64px':title.length>12?'80px':'112px';[...title].forEach(ch=>{const s=el('span');s.textContent=ch;lw.appendChild(s);});return L;}

/* the bottom screen while a game runs, as the approved mock draws it: Now playing, the title, Session / Achievements / Photos, the bar,
   the dock. o: {title, col, tab, time, total, ach:[got, all], recent:[[name, when]], shots:[css background], shotsText, dock:[icon html]} */
const GRID='<svg viewBox="0 0 20 20"><circle cx="4" cy="4" r="2"/><circle cx="10" cy="4" r="2"/><circle cx="16" cy="4" r="2"/><circle cx="4" cy="10" r="2"/><circle cx="10" cy="10" r="2"/><circle cx="16" cy="10" r="2"/><circle cx="4" cy="16" r="2"/><circle cx="10" cy="16" r="2"/><circle cx="16" cy="16" r="2"/></svg>';
const icon=(l,c,sz)=>`<span class="ic" style="width:${sz}px;height:${sz}px;background:${c};font-size:${Math.round(sz*.4)}px">${l}</span>`;
const DOCK5=[['B','#2F80ED'],['M','#E5484D'],['Y','#D93025'],['D','#5865F2'],['F','#D9912B']].map(a=>icon(a[0],a[1],44));
function companion(D,o){
 const C=D.layer('comp'),tabs=['Session','Achievements','Photos'];
 C.innerHTML='<div class="pk0">Now playing</div><div class="pt disp"></div>'+`<div class="tabs ptabs">${tabs.map((t,i)=>`<span${i===o.tab?' class="on"':''}>${t}</span>`).join('')}</div>`+
  (o.tab===0?'<div class="ptime disp"></div><div class="pl" style="top:330px">this session</div><div class="pstats"><div><b class="disp" data-r="total"></b><span>in total</span></div><div><b class="disp" data-r="ach"></b><span>achievements</span></div></div>':
   o.tab===1?'<div class="pbig disp" data-r="ach"></div><div class="pl" style="top:272px">unlocked</div>'+o.recent.map((r,i)=>`<div class="prec" style="top:${338+i*30}px"><b>${esc(r[0])}</b><span>Earned ${esc(r[1])}</span></div>`).join(''):
   o.shots.map((bg,i)=>`<div class="shot" data-shot="${i}" style="left:${40+(i%3)*188}px;top:${128+(i/3|0)*116}px;${bg}"></div>`).join('')+`<div class="pl" style="top:372px">${esc(o.shotsText)}</div>`)+
  '<div class="pbar"><i></i></div>'+`<div class="dock"><span data-dock="0">${GRID}</span>${o.dock.map((d,i)=>`<span data-dock="${i+1}" class="${d?'':'free'}">${d||''}</span>`).join('')}</div><div class="qs">Quit to shelf</div>`;
 const pt=C.querySelector('.pt');pt.textContent=o.title;pt.style.fontSize='40px';
 const X={el:C,o,ach:o.ach.slice(),fit(){pt.style.fontSize='40px';const w=pt.offsetWidth;if(w>540)pt.style.fontSize=40*540/w+'px';},
  setAch(a){X.ach[0]=a;C.querySelectorAll('[data-r="ach"]').forEach(e=>roll(e,`${a} / ${X.ach[1]}`));const f=C.querySelector('.pbar i');if(f)f.style.width=a/X.ach[1]*100+'%';},
  dockRect:i=>({left:148+i*56-9,top:471,width:62,height:62}),
  // the parts rise in (the mock's launch), and drop out when a layer opens over them
  show(anim){C.hidden=false;C.classList.remove('gone');if(anim)rise(C.children,{dy:40,stagger:45});},
  hide(){if(RM){C.hidden=true;return;}[...C.children].forEach((e,i)=>e.animate([{},{transform:'translateY(30px)',opacity:0}],{duration:200,delay:i*14,easing:EASEIN,fill:'backwards'}));C.classList.add('gone');}};
 X.fit();const tm=C.querySelector('.ptime');if(tm)roll(tm,o.time||'24:10');const tt=C.querySelector('[data-r="total"]');if(tt)roll(tt,o.total||'');X.setAch(o.ach[0]);
 D.flood(deep(o.col),310,270,true);return X;}

/* ---------- the page: mount every option, loop its steps while it is in view ---------- */
const SCREENS={},fontWait=[];
let n0=0;
function mount(host,opt,key,n){
 const fig=el('figure','opt'),D=mkDev();
 fig.innerHTML=`<figcaption class="ohead"><span class="num" aria-hidden="true">${n}</span><span class="ot"><b>${esc(opt.name)}</b><span>${esc(opt.line)}</span></span></figcaption>`;
 fig.appendChild(D.dev);
 const id=`${key}-${n}`,ctl=el('div','ctl');
 ctl.innerHTML=`<button class="btn sm" type="button" id="prev-${id}" aria-label="Previous step">◀</button><span class="step" id="step-${id}"></span>`+
  `<button class="btn sm" type="button" id="next-${id}" aria-label="Next step">▶</button><button class="btn sm play" type="button" id="play-${id}" aria-pressed="${!RM}">${RM?'Play':'Pause'}</button>`;
 fig.appendChild(ctl);host.appendChild(fig);
 const fit=()=>{const w=D.dev.clientWidth;if(w)D.rig.style.setProperty('--k',w/988);};fit();
 if('ResizeObserver' in window)new ResizeObserver(fit).observe(D.dev);else addEventListener('resize',fit);
 let api;try{api=opt.build(D);}catch(e){console.error(e&&e.stack||e);fig.appendChild(el('p','mute','This one did not draw here.'));return;}
 const S=api.states,step=ctl.querySelector('.step'),playB=ctl.querySelector('.play');let i=-1,timer=0,playing=!RM;
 const go=j=>{const from=i;i=(j+S.length)%S.length;try{api.show(i,from);}catch(e){console.error(e&&e.stack||e);}step.textContent=`${i+1} of ${S.length} · ${S[i].label}`;};
 const sched=()=>{clearTimeout(timer);if(playing&&D.vis&&!document.hidden)timer=setTimeout(()=>{go(i+1);sched();},S[i].ms||2400);};
 const setPlay=p=>{playing=p;playB.setAttribute('aria-pressed',String(p));playB.textContent=p?'Pause':'Play';sched();};
 ctl.querySelector(`#prev-${id}`).onclick=()=>{setPlay(false);go(i-1);};
 ctl.querySelector(`#next-${id}`).onclick=()=>{setPlay(false);go(i+1);};
 playB.onclick=()=>{setPlay(!playing);if(playing){go(i+1);sched();}};
 go(api.start??1);   // each opens on its first real step, so the design is on show the moment the page loads
 if(api.refit)fontWait.push(api.refit);
 if('IntersectionObserver' in window)new IntersectionObserver(e=>{D.vis=e[0].isIntersecting;sched();},{rootMargin:'120px'}).observe(fig);else{D.vis=true;sched();}
 document.addEventListener('visibilitychange',sched);}
function start(){
 for(const key in SCREENS){const host=document.getElementById('opts-'+key);if(!host)continue;host.innerHTML='';SCREENS[key].forEach((opt,i)=>mount(host,opt,key,++n0));}
 if(document.fonts&&document.fonts.ready){const face='"M PLUS Rounded 1c"';
  Promise.all(['900 40px '+face,'700 20px '+face].map(f=>document.fonts.load(f).catch(()=>0))).then(()=>fontWait.forEach(f=>{try{f();}catch(e){}}));}
 let last=0,painted=0;
 (function frame(now){requestAnimationFrame(frame);if(document.hidden){last=0;return;}if(now-painted<(RM?900:32))return;const dt=RM?0:painted?Math.min((now-painted)/1000,.08):0;painted=now;const t=RM?12:now/1000;
  for(const D of DEVS)if(D.vis)for(const f of D.frames){try{f(t,dt);}catch(e){if(!f._err){f._err=1;console.error(e&&e.stack||e);}}}})(0);
 // today's pictures, from screens-today.js
 const TD=window.BT_TODAY||{};document.querySelectorAll('img[data-today]').forEach(im=>{const src=TD[im.dataset.today];if(src)im.src=src;else{const p=el('p','mute','Today’s picture did not load here.');im.replaceWith(p);}});}

window.BTS={RM,SPRING,EASEIN,SOFT,WIPE,esc,rgb,mix,hash,el,px,deep,roll,letters,rise,pulse,stage,gameOnTop,companion,GRID,icon,DOCK5,paint,hf,whenBackdrops,lumaOf,add(key,opts){SCREENS[key]=opts;},start};
})();
