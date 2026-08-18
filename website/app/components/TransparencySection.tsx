import type { Copy } from "../content/copy";
import { GITHUB_URL } from "../content/copy";

export function TransparencySection({ text }: { text: Copy }) {
  return (
        <section className="content-section transparency-section">
          <div className="section-shell transparency-grid">
            <div className="transparency-copy">
              <p className="eyebrow"><span />{text.transparency.eyebrow}</p>
              <h2>{text.transparency.title}</h2>
              <p>{text.transparency.body}</p>
              <a href={GITHUB_URL} target="_blank" rel="noreferrer">{text.transparency.github}<span aria-hidden="true">↗</span></a>
            </div>
            <div className="transparency-list">
              {text.transparency.items.map((item, index) => (
                <article key={item.title}>
                  <span className="square-check" aria-hidden="true">✓</span>
                  <div>
                    <h3>{item.title}</h3>
                    <p>{item.text}</p>
                  </div>
                  <span className="item-index">0{index + 1}</span>
                </article>
              ))}
            </div>
          </div>
        </section>
  );
}
