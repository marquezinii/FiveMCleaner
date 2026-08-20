import type { Copy } from "../content/copy";
import { CheckMark } from "./Icons";

export function StreamersSection({ text }: { text: Copy }) {
  return (
        <section className="content-section streamer-section">
          <div className="section-shell streamer-panel">
            <div className="streamer-copy">
              <p className="eyebrow"><span />{text.streamers.eyebrow}</p>
              <h2>{text.streamers.title}</h2>
              <p>{text.streamers.body}</p>
              <p className="streamer-note"><span aria-hidden="true">i</span>{text.streamers.note}</p>
              <div className="platform-list" aria-label={text.platformsLabel}>
                {text.streamers.platforms.map((platform) => <span key={platform}>{platform}</span>)}
              </div>
            </div>

            <div className="streamer-safe-card">
              <div className="live-indicator"><span />SAFE</div>
              <h3>{text.streamers.safeTitle}</h3>
              <ul>
                {text.streamers.safeItems.map((item) => <li key={item}><CheckMark />{item}</li>)}
              </ul>
              <p>{text.streamers.honest}</p>
            </div>
          </div>
        </section>
  );
}
