import HTMLMaskElement from './html-mask-element';

export type InputElement = HTMLTextAreaElement | HTMLInputElement;
/** Bridge between InputElement and {@link Masked} */
export default class HTMLInputMaskElement extends HTMLMaskElement {
    /** InputElement to use mask on */
    input: InputElement;

    constructor(input: InputElement);

    /** Returns InputElement selection start */
    get _unsafeSelectionStart(): number | null;

    /** Returns InputElement selection end */
    get _unsafeSelectionEnd(): number | null;

    get value(): string;

    set value(value: string);

    /** Sets InputElement selection */
    _unsafeSelect(start: number, end: number): void;
}
//# sourceMappingURL=html-input-mask-element.d.ts.map