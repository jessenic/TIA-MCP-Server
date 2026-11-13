/**
 * Conservative declarations for WinCC Unified runtime APIs documented by
 * TIA-MCP-Server. This is not Siemens' complete or vendor-generated V21 API.
 * The server documents parameter shapes; return types remain unknown where
 * their exact runtime contract is not established here.
 */

type TiaMcpHmiScriptValue = boolean | number | string;

interface TiaMcpHmiTagSystemFunctions {
    InvertBitInTag(tagName: string, bitNumber: number): unknown;
    SetBitInTag(tagName: string, bitNumber: number): unknown;
    ResetBitInTag(tagName: string, bitNumber: number): unknown;
    SetTagValue(tagName: string, value: TiaMcpHmiScriptValue): unknown;
    IncreaseTag(tagName: string, stepValue: number): unknown;
    DecreaseTag(tagName: string, stepValue: number): unknown;
    LinearScaling(
        sourceTag: string,
        sourceMin: number,
        sourceMax: number,
        targetTag: string,
        targetMin: number,
        targetMax: number
    ): unknown;
    ShiftAndMask(tagName: string, shift: number, mask: number, targetTag: string): unknown;
    UpdateTag(updateId: unknown): unknown;
}

interface TiaMcpHmiUiSystemFunctions {
    ChangeScreen(screenName: string, screenWindowPath: string): unknown;
    ChangeScreenAsync(screenName: string, screenWindowPath: string): unknown;
    ChangeScreenAsyncByNumber(screenNumber: number, screenWindowPath: string): unknown;
    OpenScreenInPopup(
        popupName: string,
        screenName: string,
        title: string,
        left: number,
        top: number,
        showHeader: boolean,
        flags: unknown
    ): unknown;
    ClosePopup(popupName: string): unknown;
    ZoomIn(screenWindowPath: string, factor: number): unknown;
    ZoomOut(screenWindowPath: string, factor: number): unknown;
    ResetZoom(screenWindowPath: string): unknown;
    SetPropertyValue(itemPath: string, propertyName: string, value: unknown): unknown;
    GetPropertyValue(itemPath: string, propertyName: string): unknown;
    SetFocusOnElement(itemName: string, screenWindowPath: string): unknown;
    StartProgram(programPath: string, parameters: string): unknown;
    ShowSoftwareVersion(): unknown;
    ShowControlPanel(): unknown;
}

declare const Tags: {
    SysFct: TiaMcpHmiTagSystemFunctions;
};

declare const HMIRuntime: {
    Tags: {
        SysFct: TiaMcpHmiTagSystemFunctions;
    };
    UI: {
        SysFct: TiaMcpHmiUiSystemFunctions;
    };
};
