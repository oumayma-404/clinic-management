/* ============================================================================
   Site vitrine « Gestion Clinique » — comportements partagés (v3)
   ----------------------------------------------------------------------------
   Quatre choses, et rien de plus :
     1. Révélation au défilement, décalée.
     2. La barre de navigation gagne son filet une fois la page défilée.
     3. Le cadre du héros grandit légèrement en entrant (lié au défilement).
     4. Bascule de thème.

   Principes appliqués :
   - On n'anime que `transform` et `opacity` (propriétés compositables).
   - Le défilement est lu dans un `requestAnimationFrame`, jamais directement
     dans l'écouteur : sinon on force un recalcul de mise en page par événement.
   - `prefers-reduced-motion` ne veut pas dire « aucun retour » : la montée
     devient un fondu court (dans la CSS) et le mouvement lié au défilement est
     purement et simplement coupé.
   - L'état initial masqué est posé par la classe `.js` sur l'élément racine.
     Sans script, rien n'est caché — une page qui ne révèle jamais son contenu
     est pire qu'une page sans animation.

   ⚠️ Ce fichier doit être RÉEXÉCUTABLE. La page de prévisualisation réunit les
   cinq maquettes dans un document et n'en affiche qu'une : une page cachée n'a
   aucune mise en page, donc ses éléments ne peuvent pas être observés avant
   d'être affichés, et il faut relancer l'initialisation à chaque changement de
   page. D'où les garde-fous : les écouteurs de fenêtre sont posés une seule
   fois, ceux des éléments sont marqués par `data-bound`, et l'état vit dans
   `window.__site` plutôt que dans une fermeture qui deviendrait périmée.
   ========================================================================== */
(function () {
  'use strict';

  var root = document.documentElement;
  var reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  var S = (window.__site = window.__site || {});

  root.classList.add('js');

  /* Un élément réellement affiché. Dans la page de prévisualisation, plusieurs
     `[data-scale]` coexistent — un par maquette — et un seul est visible. */
  function visible(sel) {
    var all = document.querySelectorAll(sel);
    for (var i = 0; i < all.length; i++) {
      if (all[i].offsetParent !== null) return all[i];
    }
    return null;
  }

  /* ---------- 1 · Révélation au défilement ---------- */
  function reveal() {
    var targets = document.querySelectorAll('.rise:not(.in), .rise-g:not(.in)');
    if (!targets.length) return;

    if (!('IntersectionObserver' in window)) {
      for (var i = 0; i < targets.length; i++) targets[i].classList.add('in');
      return;
    }

    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        if (!e.isIntersecting) return;
        e.target.classList.add('in');
        io.unobserve(e.target);            // une seule fois : rien ne re-disparaît
      });
    }, { rootMargin: '0px 0px -10% 0px', threshold: 0.06 });

    for (var j = 0; j < targets.length; j++) io.observe(targets[j]);

    // Ce qui est déjà à l'écran ne doit pas attendre un défilement.
    requestAnimationFrame(function () {
      for (var k = 0; k < targets.length; k++) {
        var r = targets[k].getBoundingClientRect();
        if (r.top < window.innerHeight * 0.94) {
          targets[k].classList.add('in');
          io.unobserve(targets[k]);
        }
      }
    });
  }

  /* ---------- 2 et 3 · Barre de navigation + cadre du héros ---------- */
  function frame() {
    S.queued = false;

    if (S.nav) {
      var past = window.scrollY > 8;
      if (past !== S.nav.classList.contains('scrolled')) S.nav.classList.toggle('scrolled', past);
    }

    if (S.scaler && !reduced.matches) {
      var r = S.scaler.getBoundingClientRect();
      var vh = window.innerHeight;
      // 0 quand le haut du cadre entre par le bas, 1 quand il a monté de 65 % de
      // la hauteur de fenêtre. Borné : au-delà, on ne touche plus à rien.
      var p = (vh - r.top) / (vh * 0.65);
      p = p < 0 ? 0 : p > 1 ? 1 : p;
      S.scaler.style.transform = 'scale(' + (0.94 + 0.06 * p).toFixed(4) + ')';
    }
  }

  function onScroll() {
    if (S.queued) return;
    S.queued = true;
    requestAnimationFrame(frame);
  }

  function onReducedChange() {
    // L'utilisateur vient d'activer « mouvement réduit » : on rend la main.
    if (S.scaler && reduced.matches) S.scaler.style.transform = '';
    onScroll();
  }

  /* ---------- 4 · Bascule de thème, et extras de page ---------- */
  function bindElements() {
    // `data-bound` empêche le double abonnement : sans lui, après un changement
    // de page la bascule serait appelée deux fois et le thème ne changerait plus.
    each('[data-theme-toggle]', function (btn) {
      btn.addEventListener('click', function () {
        root.dataset.theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
      });
    });

    // Tarifs : bascule mensuel / annuel. Portée à la page visible, car la page
    // de prévisualisation contient plusieurs formulaires et plusieurs bascules.
    each('[data-period-switch]', function (sw) {
      var bM = sw.querySelector('[data-period="mois"]');
      var bA = sw.querySelector('[data-period="an"]');
      if (!bM || !bA) return;
      var scope = sw.closest('.vpage') || document;
      var set = function (annuel) {
        bM.setAttribute('aria-pressed', String(!annuel));
        bA.setAttribute('aria-pressed', String(annuel));
        eachIn(scope, '[data-mois]', function (el) {
          el.textContent = annuel ? el.getAttribute('data-an') : el.getAttribute('data-mois');
        });
        eachIn(scope, '[data-note-mois]', function (el) {
          el.textContent = annuel ? el.getAttribute('data-note-an') : el.getAttribute('data-note-mois');
        });
      };
      bM.addEventListener('click', function () { set(false); });
      bA.addEventListener('click', function () { set(true); });
    });

    // Contact : l'envoi révèle l'état de confirmation.
    each('[data-demo-form]', function (form) {
      var scope = form.closest('.vpage') || document;
      var done = scope.querySelector('[data-demo-done]');
      var back = scope.querySelector('[data-demo-back]');
      if (!done) return;
      form.addEventListener('submit', function (e) {
        e.preventDefault();
        form.hidden = true;
        done.hidden = false;
        window.scrollTo({ top: Math.max(0, done.getBoundingClientRect().top + window.scrollY - 130) });
      });
      if (back) back.addEventListener('click', function () {
        done.hidden = true;
        form.hidden = false;
      });
    });
  }

  function each(sel, fn) { eachIn(document, sel, fn, true); }
  function eachIn(scope, sel, fn, once) {
    var list = scope.querySelectorAll(sel);
    for (var i = 0; i < list.length; i++) {
      if (once) {
        if (list[i].dataset.bound) continue;
        list[i].dataset.bound = '1';
      }
      fn(list[i]);
    }
  }

  /* ---------- Initialisation ---------- */
  S.nav = visible('.nav') || document.querySelector('.nav');
  S.scaler = visible('[data-scale]');

  reveal();
  bindElements();

  if (!S.windowBound) {
    S.windowBound = true;
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onScroll, { passive: true });
    if (reduced.addEventListener) reduced.addEventListener('change', onReducedChange);
    else if (reduced.addListener) reduced.addListener(onReducedChange);
  }

  frame();
})();
