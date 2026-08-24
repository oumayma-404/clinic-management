/* ═══════════════════════════════════════════════════════════════════════════
   site.js — no framework, no dependency.

   Everything here is an ENHANCEMENT. The page is complete and readable with
   this file blocked: `.no-js` is removed on boot, and every rule that hides
   something has a `.no-js` escape.
   ═══════════════════════════════════════════════════════════════════════════ */

document.documentElement.classList.remove('no-js');

const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;

/* ── Reveal on scroll ─────────────────────────────────────────────────────
   One observer for the page. Elements reveal once and are then released. */
(() => {
  // `.rise` fades a block in; `[data-reveal]` only wants the `.in` class,
  // because sections 3 to 7 choreograph their own contents against it.
  const items = document.querySelectorAll('.rise, [data-reveal]');
  if (!items.length) return;
  if (reduced || !('IntersectionObserver' in window)) {
    items.forEach(el => el.classList.add('in'));
    return;
  }
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      e.target.classList.add('in');
      io.unobserve(e.target);
      // `will-change` pins a compositor layer. Drop it once it has landed, or
      // every revealed block on the page keeps one for the whole session.
      e.target.addEventListener('transitionend', () => e.target.classList.add('done'), { once: true });
    }
  }, { rootMargin: '0px 0px -12% 0px', threshold: 0.08 });
  items.forEach(el => io.observe(el));
})();

/* ── The top bar goes solid once the page has moved ───────────────────────
   Driven by a 1px sentinel, not by a scroll listener: a scroll listener on a
   marketing page costs a frame on every wheel tick for a boolean. */
(() => {
  const bar = document.querySelector('.topbar');
  const sentinel = document.querySelector('#top-sentinel');
  if (!bar || !sentinel || !('IntersectionObserver' in window)) return;
  new IntersectionObserver(
    ([e]) => bar.classList.toggle('stuck', !e.isIntersecting),
    { threshold: 0 }
  ).observe(sentinel);
})();

/* ── Nav dropdowns ────────────────────────────────────────────────────────
   Opens on hover with a close delay (a diagonal mouse path to the menu must
   not close it), and on click/Enter for keyboards and touch. */
(() => {
  for (const host of document.querySelectorAll('.has-menu')) {
    const btn = host.querySelector('[aria-expanded]');
    const menu = host.querySelector('.menu');
    if (!btn || !menu) continue;
    let t;

    const open = (v) => {
      clearTimeout(t);
      host.dataset.open = String(v);
      btn.setAttribute('aria-expanded', String(v));
    };

    host.addEventListener('pointerenter', (e) => { if (e.pointerType === 'mouse') open(true); });
    host.addEventListener('pointerleave', (e) => {
      if (e.pointerType !== 'mouse') return;
      clearTimeout(t);
      t = setTimeout(() => open(false), 180);
    });
    btn.addEventListener('click', () => open(host.dataset.open !== 'true'));
    host.addEventListener('focusout', () => {
      if (!host.contains(document.activeElement)) open(false);
    });
    host.addEventListener('keydown', (e) => { if (e.key === 'Escape') { open(false); btn.focus(); } });
  }
})();

/* ── The phone drawer ─────────────────────────────────────────────────── */
(() => {
  const drawer = document.querySelector('#drawer');
  const openBtn = document.querySelector('#burger');
  const closeBtn = document.querySelector('#drawer-close');
  if (!drawer || !openBtn) return;

  const set = (v) => {
    drawer.dataset.open = String(v);
    openBtn.setAttribute('aria-expanded', String(v));
    // The page behind must not scroll under an open full-screen drawer.
    document.body.style.overflow = v ? 'hidden' : '';
    if (v) drawer.querySelector('a, button')?.focus();
    else openBtn.focus();
  };

  openBtn.addEventListener('click', () => set(true));
  closeBtn?.addEventListener('click', () => set(false));
  drawer.addEventListener('click', (e) => { if (e.target.closest('a')) set(false); });
  addEventListener('keydown', (e) => { if (e.key === 'Escape' && drawer.dataset.open === 'true') set(false); });
})();

/* ── Feature zones: tabs on desktop, stacked sections below 62rem ─────────
   ⚠️ The panels must all be VISIBLE below 62rem. Hiding four of five zones
   behind a control that is not rendered at that width would silently drop
   three quarters of the section on a phone. */
(() => {
  const root = document.querySelector('[data-zones]');
  if (!root) return;
  const tabs = [...root.querySelectorAll('.rail button')];
  const panels = [...root.querySelectorAll('.panel')];
  if (!tabs.length || !panels.length) return;

  const list = root.querySelector('.rail ul');
  const wide = matchMedia('(min-width: 62rem)');
  let current = 0;

  list?.style.setProperty('--n', String(tabs.length));

  const paint = () => {
    if (!wide.matches) {
      // Stacked: every zone is open, and no tab is "selected" because the rail
      // that would show the selection is not rendered at this width.
      panels.forEach(p => { p.hidden = false; p.removeAttribute('data-active'); });
      tabs.forEach(t => t.setAttribute('aria-selected', 'false'));
      return;
    }
    // Crossfaded: all panels share one grid cell, `data-active` drives the CSS.
    // `hidden` is left off so the outgoing panel can fade rather than vanish.
    panels.forEach((p, i) => { p.hidden = false; p.dataset.active = String(i === current); });
    tabs.forEach((t, i) => t.setAttribute('aria-selected', String(i === current)));
    list?.style.setProperty('--i', String(current));
  };

  tabs.forEach((tab, i) => {
    tab.addEventListener('click', () => { current = i; paint(); });
    tab.addEventListener('keydown', (e) => {
      const d = e.key === 'ArrowDown' ? 1 : e.key === 'ArrowUp' ? -1 : 0;
      if (!d) return;
      e.preventDefault();
      current = (i + d + tabs.length) % tabs.length;
      paint();
      tabs[current].focus();
    });
  });

  wide.addEventListener('change', paint);
  paint();
})();

/* ── The numbered "who it is for" accordion ───────────────────────────────
   One open at a time, the first open on load. Without JS every item is open
   (CSS keeps `grid-template-rows: 0fr` off until `data-open` exists), so the
   copy is never hidden from a reader or a crawler. */
(() => {
  const list = document.querySelector('[data-serve]');
  if (!list) return;
  const items = [...list.querySelectorAll('.serve-item')];
  if (!items.length) return;

  const open = (idx) => items.forEach((it, i) => {
    const on = i === idx;
    it.dataset.open = String(on);
    it.querySelector('button')?.setAttribute('aria-expanded', String(on));
  });

  items.forEach((it, i) => it.querySelector('button')?.addEventListener('click', () => {
    open(it.dataset.open === 'true' ? -1 : i);
  }));
  open(0);
})();


/* ── FAQ accordion ────────────────────────────────────────────────────────
   Same contract as the "who it is for" list: open by default in CSS, so the
   answers are readable with the script blocked. */
(() => {
  const list = document.querySelector('[data-faq]');
  if (!list) return;
  const items = [...list.querySelectorAll('.faq-item')];
  if (!items.length) return;

  const set = (idx) => items.forEach((it, i) => {
    const on = i === idx;
    it.dataset.open = String(on);
    it.querySelector('button')?.setAttribute('aria-expanded', String(on));
  });

  items.forEach((it, i) => it.querySelector('button')?.addEventListener('click', () => {
    set(it.dataset.open === 'true' ? -1 : i);
  }));
  set(0);
})();

/* ── Figures that count up ────────────────────────────────────────────────
   Only the chips, only once, only when they are on screen. The final value is
   already in the HTML, so a blocked script or a reduced-motion reader simply
   sees the number — nothing here is the source of truth. */
(() => {
  const els = [...document.querySelectorAll('[data-count]')];
  if (!els.length || reduced || !('IntersectionObserver' in window)) return;

  const run = (el) => {
    const target = parseFloat(el.dataset.count);
    if (!Number.isFinite(target)) return;
    const final = el.textContent;
    // Keep the box the size of its final value or the card reflows every frame.
    el.style.minWidth = el.getBoundingClientRect().width + 'px';
    const dur = 1100, t0 = performance.now();
    const fmt = new Intl.NumberFormat('fr-FR');
    const tick = (now) => {
      const p = Math.min(1, (now - t0) / dur);
      // The same ease-out the reveals use, so they read as one system.
      const e = 1 - Math.pow(1 - p, 3);
      if (p < 1) {
        el.textContent = fmt.format(Math.round(target * e)).replace(/ | /g, ' ');
        requestAnimationFrame(tick);
      } else {
        el.textContent = final;   // restore the exact authored string
        el.style.minWidth = '';
      }
    };
    requestAnimationFrame(tick);
  };

  const io = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      io.unobserve(e.target);
      setTimeout(() => run(e.target), 260);
    }
  }, { threshold: 0.6 });
  els.forEach(el => io.observe(el));
})();


/* ── Profile switcher (« à qui il s’adresse ») ─────────────────────────────
   Shipped inside the section block’s stylesheet by the builder; moved here,
   because `})();` inside a CSS file ends parsing and drops every rule after
   it — which is exactly what happened to the constat band’s layout. */
/* ── The "à qui il s'adresse" card follows the open profile ───────────────
   The accordion above already opens one row at a time; this only points the
   card at whichever profile the reader last reached for.

   Nothing here is a source of content. With the script blocked `data-stack`
   is never stamped, the five cards stay in normal flow one under the other,
   and every profile is read in full — the same contract the accordion itself
   keeps by defaulting to open. */
(() => {
  const list = document.querySelector('[data-serve]');
  const stage = document.querySelector('[data-serve-stage]');
  if (!list || !stage) return;

  const items = [...list.querySelectorAll('.serve-item')];
  const faces = [...stage.querySelectorAll('.sv-face')];
  // A row without its card, or the other way round, would silently hide a
  // profile. Leave the readable stack alone rather than half-wire it.
  if (!faces.length || faces.length !== items.length) return;

  stage.dataset.stack = 'true';

  let current = -1;
  const show = (i) => {
    if (i === current) return;
    current = i;
    faces.forEach((face, n) => {
      const on = n === i;
      face.dataset.active = String(on);
      // `visibility` already takes the others out of the tree; this covers the
      // frames in which the outgoing card is still fading.
      face.setAttribute('aria-hidden', String(!on));
    });
  };

  // Read from the click, not from `data-open`, so this does not depend on
  // running after the accordion's own listener. And whether the click opened
  // the row or closed it, the card follows it: blanking it on a close would
  // empty half the section for a reason the reader cannot see.
  items.forEach((item, i) => item.querySelector('button')?.addEventListener('click', () => show(i)));

  const open = items.findIndex(it => it.dataset.open === 'true');
  show(open < 0 ? 0 : open);
})();

/* ── « Le quotidien »: six moments on a strip ─────────────────────────────
   ⚠️ The core interaction is NOT this file. The strip is a native
   scroll-snap scroller, so the swipe on a phone, the trackpad on a laptop and
   the arrow keys on the focused track all work with the script blocked. What
   is added here is the numbered rail, the two arrows, and marking the visible
   slide so its diagram plays.

   ⚠️ Two shapes were tried before this and both were wrong. Six panels
   behind a STICKY pane pinned the page for six screens and read as a browser
   that had stopped responding. Six cards in the flow fixed that and cost
   4 300 px — four and a half screens for section 2. A strip is ~1 000 px and
   takes nothing from the reader: the page scrolls normally past it and the
   control is visible. Do not go back to either.

   The observer is what marks the current slide, not the click handler, so the
   rail stays right however the strip was moved — a click, a swipe, a key, or
   the browser restoring a scroll position on reload. */
(() => {
  const root = document.querySelector('[data-jour]');
  if (!root) return;
  const track = root.querySelector('.jour-track');
  const items = [...root.querySelectorAll('.jour-item')];
  const dots = [...root.querySelectorAll('.jour-dots button')];
  const arrows = [...root.querySelectorAll('.jour-arrow')];
  if (!track || !items.length) return;

  root.dataset.slider = 'on';
  let current = 0;

  const mark = (i) => {
    if (i < 0 || i === current) return;
    current = i;
    dots.forEach((d, n) => d.setAttribute('aria-current', n === i ? 'true' : 'false'));
    // Re-arm the interval with the slide, so the number that is filling and the
    // move it is counting down to cannot drift apart.
    if (timer) { clearInterval(timer); timer = setInterval(tick, DWELL); }
  };

  // The strip WRAPS. It used to disable the arrow at each end, which is right
  // for a strip you drive yourself and wrong for one that moves on its own:
  // the automatic advance has to get from the sixth moment back to the first.
  const wrap = (i) => (i + items.length) % items.length;

  const go = (i) => {
    const from = current;
    i = wrap(i);
    // Measured, not `offsetLeft`: the slides' offset parent is not guaranteed
    // to be the track, and a wrong parent scrolls to the wrong place silently.
    const left = track.scrollLeft
      + items[i].getBoundingClientRect().left - track.getBoundingClientRect().left;
    // ⚠️ Smooth only between NEIGHBOURS. The automatic advance wraps from the
    // sixth moment to the first, and smooth-scrolling that distance rewinds
    // the reader through all six panels — six seconds of nothing readable. A
    // jump reads as « back to the start », which is what it is. The same
    // applies to a rail click from 01 to 06.
    const near = Math.abs(i - from) === 1;
    track.scrollTo({ left, behavior: (reduced || !near) ? 'auto' : 'smooth' });
  };

  /* ── It advances on its own ──────────────────────────────────────────────
     Two rules decide everything here. It only runs while the section is on
     screen — a strip cycling in a part of the page nobody is looking at is
     pure waste — and it STOPS FOR GOOD the first time the reader touches a
     control. A carousel that resumes after you have steered it is a carousel
     arguing with you.

     Reduced motion turns it off entirely: an automatic change of view is
     exactly the kind of unrequested movement that setting is asking about. */
  const DWELL = 5500;
  let timer = null, seen = false, taken = reduced, held = false;

  const stop = () => { clearInterval(timer); timer = null; root.dataset.auto = 'off'; };
  const tick = () => { if (!taken) go(current + 1); };
  const sync = () => {
    if (taken || !seen) { clearInterval(timer); timer = null; return; }
    if (held) { root.dataset.auto = 'paused'; clearInterval(timer); timer = null; return; }
    root.dataset.auto = 'on';
    clearInterval(timer);
    timer = setInterval(tick, DWELL);
  };
  // Restarting the interval on every move is what keeps the filling number and
  // the actual advance in step; without it a click mid-dwell leaves the fill
  // running against a timer that is about to fire.
  const take = () => { taken = true; stop(); };

  /* ⚠️ It does NOT pause on hover, and that is deliberate. Pausing on hover is
     the conventional courtesy and here it broke the feature outright: a reader
     scrolls down, the section arrives under a pointer that has not moved, the
     browser fires `pointerenter`, and the strip never advances once. Measured —
     stuck on 01 with `data-auto="paused"` for as long as you like.

     What does hold it is a keyboard focus inside it (moving the view under
     someone who is tabbing is worse than useless) and the tab being hidden. */
  root.addEventListener('focusin', () => { held = true; sync(); });
  root.addEventListener('focusout', () => { held = false; sync(); });
  document.addEventListener('visibilitychange', () => { held = document.hidden; sync(); });

  if ('IntersectionObserver' in window) {
    new IntersectionObserver((es) => {
      seen = es[0].isIntersecting;
      sync();
    }, { threshold: 0.35 }).observe(root);
  }

  dots.forEach((d) => d.addEventListener('click', () => { take(); go(Number(d.dataset.go)); }));
  arrows.forEach((btn) => btn.addEventListener('click', () => { take(); go(current + Number(btn.dataset.dir)); }));
  /* A swipe is the reader steering too, even though no button was pressed —
     and so is an arrow key on the focused strip, and so is the paper/software
     switch on a phone.
     ⚠️ `pointerdown` on the track was tried and is too wide: on a desktop that
     is any click anywhere on the panel, including a click that was only meant
     to put the window in front. Touch and keys only. */
  track.addEventListener('touchstart', take, { passive: true });
  track.addEventListener('keydown', take);
  root.querySelectorAll('.jd-switch button').forEach((b) => b.addEventListener('click', take));

  /* Below 62rem the two halves of a panel share one cell and only one is on
     screen. It flips itself once, shortly after the slide arrives, so the
     answer does not depend on the reader finding a control — and a tap cancels
     that for good, because a control that keeps moving on its own after you
     have used it is a control fighting you. */
  const reveal = items.map((it) => {
    const fig = it.querySelector('.jour-shot--jd');
    const btns = [...it.querySelectorAll('.jd-switch button')];
    if (!fig || !btns.length) return () => {};
    let timer = null, taken = false;
    const set = (n) => {
      fig.dataset.flip = String(n);
      btns.forEach((b, i) => b.setAttribute('aria-pressed', i === n ? 'true' : 'false'));
    };
    btns.forEach((b, i) => b.addEventListener('click', () => {
      taken = true; clearTimeout(timer); set(i);
    }));
    return () => {
      if (taken) return;
      if (reduced) { set(1); return; }
      clearTimeout(timer);
      timer = setTimeout(() => { if (!taken) set(1); }, 1400);
    };
  });

  if (reduced || !('IntersectionObserver' in window)) {
    items.forEach((it, n) => { it.dataset.active = 'true'; reveal[n](); });
  } else {
    const io = new IntersectionObserver((entries) => {
      for (const e of entries) if (e.isIntersecting) {
        const n = items.indexOf(e.target);
        e.target.dataset.active = 'true';       // stays lit: a moment already
        mark(n);                                // read does not replay on return
        reveal[n]();
      }
    }, { root: track, threshold: 0.6 });
    items.forEach((it) => { it.dataset.active = 'false'; io.observe(it); });
  }

  current = -1;
  mark(0);
})();

/* ── The numbered "who it is for" accordion ───────────────────────────────
   One open at a time, the first open on load. Without JS every item is open
   (CSS keeps `grid-template-rows: 0fr` off until `data-open` exists), so the
   copy is never hidden from a reader or a crawler. */
(() => {
  const list = document.querySelector('[data-serve]');
  if (!list) return;
  const items = [...list.querySelectorAll('.serve-item')];
  if (!items.length) return;

  const open = (idx) => items.forEach((it, i) => {
    const on = i === idx;
    it.dataset.open = String(on);
    it.querySelector('button')?.setAttribute('aria-expanded', String(on));
  });

  items.forEach((it, i) => it.querySelector('button')?.addEventListener('click', () => {
    open(it.dataset.open === 'true' ? -1 : i);
  }));
  open(0);
})();


/* ── FAQ accordion ────────────────────────────────────────────────────────
   Same contract as the "who it is for" list: open by default in CSS, so the
   answers are readable with the script blocked. */
(() => {
  const list = document.querySelector('[data-faq]');
  if (!list) return;
  const items = [...list.querySelectorAll('.faq-item')];
  if (!items.length) return;

  const set = (idx) => items.forEach((it, i) => {
    const on = i === idx;
    it.dataset.open = String(on);
    it.querySelector('button')?.setAttribute('aria-expanded', String(on));
  });

  items.forEach((it, i) => it.querySelector('button')?.addEventListener('click', () => {
    set(it.dataset.open === 'true' ? -1 : i);
  }));
  set(0);
})();

/* ── Figures that count up ────────────────────────────────────────────────
   Only the chips, only once, only when they are on screen. The final value is
   already in the HTML, so a blocked script or a reduced-motion reader simply
   sees the number — nothing here is the source of truth. */
(() => {
  const els = [...document.querySelectorAll('[data-count]')];
  if (!els.length || reduced || !('IntersectionObserver' in window)) return;

  const run = (el) => {
    const target = parseFloat(el.dataset.count);
    if (!Number.isFinite(target)) return;
    const final = el.textContent;
    // Keep the box the size of its final value or the card reflows every frame.
    el.style.minWidth = el.getBoundingClientRect().width + 'px';
    const dur = 1100, t0 = performance.now();
    const fmt = new Intl.NumberFormat('fr-FR');
    const tick = (now) => {
      const p = Math.min(1, (now - t0) / dur);
      // The same ease-out the reveals use, so they read as one system.
      const e = 1 - Math.pow(1 - p, 3);
      if (p < 1) {
        el.textContent = fmt.format(Math.round(target * e)).replace(/ | /g, ' ');
        requestAnimationFrame(tick);
      } else {
        el.textContent = final;   // restore the exact authored string
        el.style.minWidth = '';
      }
    };
    requestAnimationFrame(tick);
  };

  const io = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      io.unobserve(e.target);
      setTimeout(() => run(e.target), 260);
    }
  }, { threshold: 0.6 });
  els.forEach(el => io.observe(el));
})();


/* ── Profile switcher (« à qui il s’adresse ») ─────────────────────────────
   Shipped inside the section block’s stylesheet by the builder; moved here,
   because `})();` inside a CSS file ends parsing and drops every rule after
   it — which is exactly what happened to the constat band’s layout. */
/* ── The "à qui il s'adresse" card follows the open profile ───────────────
   The accordion above already opens one row at a time; this only points the
   card at whichever profile the reader last reached for.

   Nothing here is a source of content. With the script blocked `data-stack`
   is never stamped, the five cards stay in normal flow one under the other,
   and every profile is read in full — the same contract the accordion itself
   keeps by defaulting to open. */
(() => {
  const list = document.querySelector('[data-serve]');
  const stage = document.querySelector('[data-serve-stage]');
  if (!list || !stage) return;

  const items = [...list.querySelectorAll('.serve-item')];
  const faces = [...stage.querySelectorAll('.sv-face')];
  // A row without its card, or the other way round, would silently hide a
  // profile. Leave the readable stack alone rather than half-wire it.
  if (!faces.length || faces.length !== items.length) return;

  stage.dataset.stack = 'true';

  let current = -1;
  const show = (i) => {
    if (i === current) return;
    current = i;
    faces.forEach((face, n) => {
      const on = n === i;
      face.dataset.active = String(on);
      // `visibility` already takes the others out of the tree; this covers the
      // frames in which the outgoing card is still fading.
      face.setAttribute('aria-hidden', String(!on));
    });
  };

  // Read from the click, not from `data-open`, so this does not depend on
  // running after the accordion's own listener. And whether the click opened
  // the row or closed it, the card follows it: blanking it on a close would
  // empty half the section for a reason the reader cannot see.
  items.forEach((item, i) => item.querySelector('button')?.addEventListener('click', () => show(i)));

  const open = items.findIndex(it => it.dataset.open === 'true');
  show(open < 0 ? 0 : open);
})();

/* ── « Le quotidien »: six moments, each playing as it arrives ───────
   Each card is marked `data-active` once, when it is a third of the way into
   the viewport, and every entrance in CSS hangs off that one attribute.

   ⚠️ This replaced a STICKY version that pinned one pane for six screens.
   It animated well and it read as a frozen page: the wheel turns, nothing
   moves, and a reader who cannot tell a designed pause from a bug leaves.
   Do not put it back. If the section has to be shortened, cut moments — not
   the reader's control of the scroll.

   ⚠️ An ENHANCEMENT, and the fallback is not a degraded version. With the
   script blocked `.no-js` shows every card in its settled state, and under
   reduced motion all six are marked active at once with no observer at all.
   Nothing here is a source of content. */
(() => {
  const items = [...document.querySelectorAll('[data-jour] .jour-item')];
  if (!items.length) return;

  const light = (it) => { it.dataset.active = 'true'; };

  if (reduced || !('IntersectionObserver' in window)) { items.forEach(light); return; }

  // A third of the way up: the card is committed to the screen before it
  // starts, so nothing plays out of sight and nothing waits until it is past.
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) if (e.isIntersecting) { light(e.target); io.unobserve(e.target); }
  }, { rootMargin: '0px 0px -33% 0px', threshold: 0 });
  items.forEach((it) => { it.dataset.active = 'false'; io.observe(it); });
})();

/* ── The numbered "who it is for" accordion ───────────────────────────────
   One open at a time, the first open on load. Without JS every item is open
   (CSS keeps `grid-template-rows: 0fr` off until `data-open` exists), so the
   copy is never hidden from a reader or a crawler. */
(() => {
  const list = document.querySelector('[data-serve]');
  if (!list) return;
  const items = [...list.querySelectorAll('.serve-item')];
  if (!items.length) return;

  const open = (idx) => items.forEach((it, i) => {
    const on = i === idx;
    it.dataset.open = String(on);
    it.querySelector('button')?.setAttribute('aria-expanded', String(on));
  });

  items.forEach((it, i) => it.querySelector('button')?.addEventListener('click', () => {
    open(it.dataset.open === 'true' ? -1 : i);
  }));
  open(0);
})();


/* ── FAQ accordion ────────────────────────────────────────────────────────
   Same contract as the "who it is for" list: open by default in CSS, so the
   answers are readable with the script blocked. */
(() => {
  const list = document.querySelector('[data-faq]');
  if (!list) return;
  const items = [...list.querySelectorAll('.faq-item')];
  if (!items.length) return;

  const set = (idx) => items.forEach((it, i) => {
    const on = i === idx;
    it.dataset.open = String(on);
    it.querySelector('button')?.setAttribute('aria-expanded', String(on));
  });

  items.forEach((it, i) => it.querySelector('button')?.addEventListener('click', () => {
    set(it.dataset.open === 'true' ? -1 : i);
  }));
  set(0);
})();

/* ── Figures that count up ────────────────────────────────────────────────
   Only the chips, only once, only when they are on screen. The final value is
   already in the HTML, so a blocked script or a reduced-motion reader simply
   sees the number — nothing here is the source of truth. */
(() => {
  const els = [...document.querySelectorAll('[data-count]')];
  if (!els.length || reduced || !('IntersectionObserver' in window)) return;

  const run = (el) => {
    const target = parseFloat(el.dataset.count);
    if (!Number.isFinite(target)) return;
    const final = el.textContent;
    // Keep the box the size of its final value or the card reflows every frame.
    el.style.minWidth = el.getBoundingClientRect().width + 'px';
    const dur = 1100, t0 = performance.now();
    const fmt = new Intl.NumberFormat('fr-FR');
    const tick = (now) => {
      const p = Math.min(1, (now - t0) / dur);
      // The same ease-out the reveals use, so they read as one system.
      const e = 1 - Math.pow(1 - p, 3);
      if (p < 1) {
        el.textContent = fmt.format(Math.round(target * e)).replace(/ | /g, ' ');
        requestAnimationFrame(tick);
      } else {
        el.textContent = final;   // restore the exact authored string
        el.style.minWidth = '';
      }
    };
    requestAnimationFrame(tick);
  };

  const io = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      io.unobserve(e.target);
      setTimeout(() => run(e.target), 260);
    }
  }, { threshold: 0.6 });
  els.forEach(el => io.observe(el));
})();


/* ── Profile switcher (« à qui il s’adresse ») ─────────────────────────────
   Shipped inside the section block’s stylesheet by the builder; moved here,
   because `})();` inside a CSS file ends parsing and drops every rule after
   it — which is exactly what happened to the constat band’s layout. */
/* ── The "à qui il s'adresse" card follows the open profile ───────────────
   The accordion above already opens one row at a time; this only points the
   card at whichever profile the reader last reached for.

   Nothing here is a source of content. With the script blocked `data-stack`
   is never stamped, the five cards stay in normal flow one under the other,
   and every profile is read in full — the same contract the accordion itself
   keeps by defaulting to open. */
(() => {
  const list = document.querySelector('[data-serve]');
  const stage = document.querySelector('[data-serve-stage]');
  if (!list || !stage) return;

  const items = [...list.querySelectorAll('.serve-item')];
  const faces = [...stage.querySelectorAll('.sv-face')];
  // A row without its card, or the other way round, would silently hide a
  // profile. Leave the readable stack alone rather than half-wire it.
  if (!faces.length || faces.length !== items.length) return;

  stage.dataset.stack = 'true';

  let current = -1;
  const show = (i) => {
    if (i === current) return;
    current = i;
    faces.forEach((face, n) => {
      const on = n === i;
      face.dataset.active = String(on);
      // `visibility` already takes the others out of the tree; this covers the
      // frames in which the outgoing card is still fading.
      face.setAttribute('aria-hidden', String(!on));
    });
  };

  // Read from the click, not from `data-open`, so this does not depend on
  // running after the accordion's own listener. And whether the click opened
  // the row or closed it, the card follows it: blanking it on a close would
  // empty half the section for a reason the reader cannot see.
  items.forEach((item, i) => item.querySelector('button')?.addEventListener('click', () => show(i)));

  const open = items.findIndex(it => it.dataset.open === 'true');
  show(open < 0 ? 0 : open);
})();

