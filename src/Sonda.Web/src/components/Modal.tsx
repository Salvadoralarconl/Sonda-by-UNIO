import { useEffect, useRef, type ReactNode } from 'react';
import './modal.css';

export function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null);
  const close = useRef(onClose);
  close.current = onClose;
  useEffect(() => {
    const dialog = ref.current!;
    const opener = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    const x = window.scrollX, y = window.scrollY;
    dialog.showModal();
    document.body.style.overflow = 'hidden';
    return () => {
      dialog.close();
      document.body.style.overflow = previousOverflow;
      window.scrollTo(x, y);
      opener?.focus({ preventScroll: true });
    };
  }, []);
  return <dialog ref={ref} className="sonda-modal" aria-label={title} onCancel={event => { event.preventDefault(); close.current(); }}>
    <button className="modal-close" aria-label={`Close ${title}`} onClick={onClose}>×</button>
    {children}
  </dialog>;
}
