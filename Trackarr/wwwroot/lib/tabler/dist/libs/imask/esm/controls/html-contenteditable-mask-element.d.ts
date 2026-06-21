import HTMLMaskElement from './html-mask-element';

export default class HTMLContenteditableMaskElement extends HTMLMaskElement {
    input: HTMLElement;

    /** Returns HTMLElement selection start */
    get _unsafeSelectionStart(): number | null;

    /** Returns HTMLElement selection end */
    get _unsafeSelectionEnd(): number | null;

    /** HTMLElement value */
    get value(): string;

    set value(value: string);

    /** Sets HTMLElement selection */
    _unsafeSelect(start: number, end: number): void;
}
//# sourceMappingURL=html-contenteditable-mask-element.d.ts.map