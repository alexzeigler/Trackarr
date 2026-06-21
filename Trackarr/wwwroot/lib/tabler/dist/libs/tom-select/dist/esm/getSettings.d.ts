import {RecursivePartial, TomInput, TomSettings} from './types/index.ts';

export default function getSettings(input: TomInput, settings_user: RecursivePartial<TomSettings>): TomSettings;
