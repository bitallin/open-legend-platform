(function () {
  const box = document.getElementById('lightbox');
  if (!box) return;
  const img = box.querySelector('img');
  const close = document.getElementById('lightbox-close');
  function hide() { box.hidden = true; img.removeAttribute('src'); }
  close && close.addEventListener('click', hide);
  box.addEventListener('click', function (e) { if (e.target === box) hide(); });
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape') hide(); });
  document.addEventListener('click', function (e) {
    const t = e.target;
    if (t && t.classList && t.classList.contains('lightbox-src') && t.tagName === 'IMG') {
      e.preventDefault();
      // prefer full image: strip thumb=1
      let src = t.getAttribute('src') || '';
      src = src.replace('&thumb=1', '').replace('?thumb=1', '?').replace(/\?$/, '');
      img.src = src || t.src;
      box.hidden = false;
    }
  });
})();
