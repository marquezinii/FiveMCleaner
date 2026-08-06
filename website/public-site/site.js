const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
const progress = document.querySelector(".scroll-progress span");
const stage = document.querySelector("[data-tilt]");
const appWindow = stage?.querySelector(".app-window");

function updateScrollProgress() {
  const scrollable = document.documentElement.scrollHeight - innerHeight;
  const ratio = scrollable > 0 ? scrollY / scrollable : 0;
  progress?.style.setProperty("transform", `scaleX(${ratio})`);
}

function resetTilt() {
  appWindow?.style.removeProperty("--tilt");
}

if (stage && appWindow && !reducedMotion.matches) {
  stage.addEventListener("pointermove", (event) => {
    const bounds = stage.getBoundingClientRect();
    const x = (event.clientX - bounds.left) / bounds.width - 0.5;
    const y = (event.clientY - bounds.top) / bounds.height - 0.5;
    appWindow.style.setProperty(
      "--tilt",
      `rotateX(${(-y * 3).toFixed(2)}deg) rotateY(${(x * 5).toFixed(2)}deg) translate3d(${(x * 5).toFixed(2)}px, ${(y * 4).toFixed(2)}px, 0)`,
    );
  });
  stage.addEventListener("pointerleave", resetTilt);
}

const observer = new IntersectionObserver(
  (entries) => {
    for (const entry of entries) {
      if (entry.isIntersecting) {
        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      }
    }
  },
  { threshold: 0.12 },
);

document.querySelectorAll(".reveal").forEach((element) => observer.observe(element));
addEventListener("scroll", updateScrollProgress, { passive: true });
reducedMotion.addEventListener("change", resetTilt);
updateScrollProgress();
