import Image from "next/image";
import type { Copy } from "../content/copy";
import { ICON_URL } from "../content/copy";

export function FaqSection({ text }: { text: Copy }) {
  return (
        <section className="content-section faq-section" id="faq">
          <div className="section-shell faq-layout">
            <div className="faq-heading">
              <p className="eyebrow"><span />{text.faq.eyebrow}</p>
              <h2>{text.faq.title}</h2>
              <Image src={ICON_URL} width={108} height={108} alt="" unoptimized />
            </div>
            <div className="faq-list">
              {text.faq.items.map(([question, answer], index) => (
                <details key={question} open={index === 0}>
                  <summary>
                    <span>{question}</span>
                    <span className="faq-plus" aria-hidden="true">+</span>
                  </summary>
                  <p>{answer}</p>
                </details>
              ))}
            </div>
          </div>
        </section>
  );
}
