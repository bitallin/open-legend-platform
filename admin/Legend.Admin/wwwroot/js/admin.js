(function () {
  // Sidebar collapse
  var sidebar = document.getElementById('sidebar');
  var toggle = document.getElementById('sidebar-toggle');
  try {
    if (sidebar && localStorage.getItem('ol-admin-sidebar') === '1')
      sidebar.classList.add('collapsed');
  } catch (e) {}
  if (toggle && sidebar) {
    toggle.addEventListener('click', function () {
      sidebar.classList.toggle('collapsed');
      try {
        localStorage.setItem('ol-admin-sidebar', sidebar.classList.contains('collapsed') ? '1' : '0');
      } catch (e) {}
    });
  }

  // Lightbox
  var box = document.getElementById('lightbox');
  if (box) {
    var img = box.querySelector('img');
    var close = document.getElementById('lightbox-close');
    function hide() { box.hidden = true; img.removeAttribute('src'); }
    close && close.addEventListener('click', hide);
    box.addEventListener('click', function (e) { if (e.target === box) hide(); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') hide(); });
    document.addEventListener('click', function (e) {
      var t = e.target;
      if (t && t.classList && t.classList.contains('lightbox-src') && t.tagName === 'IMG') {
        e.preventDefault();
        e.stopPropagation();
        var src = t.getAttribute('src') || '';
        src = src.replace(/([&?])thumb=(1|true)/g, '$1').replace(/\?&/, '?').replace(/[?&]$/, '');
        img.src = src || t.src;
        box.hidden = false;
      }
    });
  }

  // Dropzone
  document.querySelectorAll('.dropzone').forEach(function (dz) {
    var input = dz.querySelector('input[type=file]');
    if (!input) return;
    dz.addEventListener('click', function (e) {
      if (e.target.tagName !== 'INPUT' && e.target.tagName !== 'BUTTON' && e.target.tagName !== 'SELECT' && e.target.tagName !== 'LABEL')
        input.click();
    });
    ['dragenter', 'dragover'].forEach(function (ev) {
      dz.addEventListener(ev, function (e) { e.preventDefault(); dz.classList.add('dragover'); });
    });
    ['dragleave', 'drop'].forEach(function (ev) {
      dz.addEventListener(ev, function (e) { e.preventDefault(); dz.classList.remove('dragover'); });
    });
    dz.addEventListener('drop', function (e) {
      if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length) {
        input.files = e.dataTransfer.files;
        var form = dz.closest('form');
        if (form && dz.getAttribute('data-autosubmit') === '1') form.submit();
      }
    });
  });

  // Asset multi-select bulk bar
  function refreshBulk() {
    var bar = document.getElementById('bulk-bar');
    if (!bar) return;
    var boxes = document.querySelectorAll('.asset-select:checked');
    var count = boxes.length;
    bar.classList.toggle('visible', count > 0);
    var label = document.getElementById('bulk-count');
    if (label) label.textContent = '已选 ' + count + ' 项';
    document.querySelectorAll('.asset-card').forEach(function (c) {
      var cb = c.querySelector('.asset-select');
      c.classList.toggle('selected', !!(cb && cb.checked));
    });
  }
  document.addEventListener('change', function (e) {
    if (e.target && e.target.classList && e.target.classList.contains('asset-select'))
      refreshBulk();
  });
  refreshBulk();

  // Live preview for asset select fields
  document.querySelectorAll('[data-preview-target]').forEach(function (sel) {
    var targetId = sel.getAttribute('data-preview-target');
    var frame = document.getElementById(targetId);
    if (!frame) return;
    function sync() {
      var id = sel.value;
      var img = frame.querySelector('img');
      var empty = frame.querySelector('.empty-art, .slot-empty, .preview-empty');
      if (!id) {
        if (img) img.remove();
        if (empty) empty.hidden = false;
        else if (!frame.querySelector('.preview-empty')) {
          var d = document.createElement('div');
          d.className = 'preview-empty empty-art';
          d.innerHTML = '<strong>未绑定外观</strong><span>选择素材后在此预览</span>';
          frame.appendChild(d);
        }
        return;
      }
      if (empty) empty.hidden = true;
      if (!img) {
        img = document.createElement('img');
        img.className = 'lightbox-src';
        frame.appendChild(img);
      }
      img.src = '/api/media/' + encodeURIComponent(id) + '?thumb=true';
      img.alt = id;
    }
    sel.addEventListener('change', sync);
    sync();
  });
})();
