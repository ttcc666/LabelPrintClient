(() => {
  const body = document.body;
  const toggle = document.querySelector('[data-sidebar-toggle]');
  const closeTargets = document.querySelectorAll('[data-sidebar-close], .sidebar-item');

  if (!toggle) {
    return;
  }

  const setOpen = (isOpen) => {
    body.classList.toggle('sidebar-open', isOpen);
    toggle.setAttribute('aria-expanded', String(isOpen));
  };

  toggle.addEventListener('click', () => {
    setOpen(!body.classList.contains('sidebar-open'));
  });

  closeTargets.forEach((target) => {
    target.addEventListener('click', () => setOpen(false));
  });

  window.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') {
      setOpen(false);
    }
  });
})();
